namespace StockSharp.MatchingEngine;

/// <summary>
/// Stop order manager implementation.
/// </summary>
/// <remarks>
/// Thread-safe. The server drives a single instance concurrently — Register / Cancel / Replace run
/// on the order-command path while CheckPrice runs on the feeder tick pump — so every operation is
/// serialized under one lock. Without it a register racing a check corrupts the per-security index
/// (lost stops / index-out-of-range) and two concurrent checks double-trigger the same stop.
/// This single-lock serialization is a required invariant that must be maintained: every mutation
/// path (Register / Cancel / Replace) and the price-check path (CheckPrice) must continue to run under
/// the same lock so the per-security index and the one-shot trigger semantics stay consistent.
/// </remarks>
public class StopOrderManager : IStopOrderManager
{
	private readonly Dictionary<long, StopOrderInfo> _stopOrders = [];
	private readonly Dictionary<SecurityId, List<long>> _bySecurityId = [];
	private readonly Lock _sync = new();

	/// <inheritdoc />
	public void Register(StopOrderInfo info)
	{
		if (info is null)
			throw new ArgumentNullException(nameof(info));

		using (_sync.EnterScope())
		{
			_stopOrders[info.TransactionId] = info;

			if (!_bySecurityId.TryGetValue(info.SecurityId, out var list))
				_bySecurityId[info.SecurityId] = list = [];

			list.Add(info.TransactionId);
		}
	}

	/// <inheritdoc />
	public bool Cancel(long transactionId, out StopOrderInfo info)
	{
		using (_sync.EnterScope())
		{
			if (!_stopOrders.TryGetValue(transactionId, out info))
				return false;

			_stopOrders.Remove(transactionId);

			if (_bySecurityId.TryGetValue(info.SecurityId, out var list))
			{
				list.Remove(transactionId);

				if (list.Count == 0)
					_bySecurityId.Remove(info.SecurityId);
			}

			return true;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The cancel-then-register swap is performed atomically under the reentrant lock, so a concurrent
	/// <see cref="CheckPrice"/> never observes a window in which neither the old stop nor its
	/// replacement is live.
	/// </remarks>
	public bool Replace(long origTransactionId, StopOrderInfo newInfo)
	{
		// The lock is reentrant: hold it across Cancel + Register so the replace is atomic against a
		// concurrent CheckPrice (no window where the old stop is gone but the new one is not yet in).
		using (_sync.EnterScope())
		{
			if (!Cancel(origTransactionId, out _))
				return false;

			Register(newInfo);
			return true;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Stop orders are indexed per security: this evaluates only the stops registered for
	/// <paramref name="securityId"/> on each incoming price tick. For every such stop it first refreshes
	/// any trailing stop price (see <see cref="UpdateTrailing"/>) and then tests the trigger condition
	/// (see <see cref="IsTriggered"/>); each newly triggered stop is returned together with the resulting
	/// order to submit to the matcher (see <see cref="CreateResultingOrder"/>). Triggering is one-shot: a
	/// triggered stop is removed from both the transaction-id map and the per-security index, so it is
	/// never returned on a later tick. Returns an empty list when the tick triggers nothing.
	/// </remarks>
	public IReadOnlyList<StopOrderTrigger> CheckPrice(SecurityId securityId, decimal price, DateTime time)
	{
		using (_sync.EnterScope())
		{
			if (!_bySecurityId.TryGetValue(securityId, out var list) || list.Count == 0)
				return [];

			List<StopOrderTrigger> triggered = null;

			for (var i = list.Count - 1; i >= 0; i--)
			{
				var transId = list[i];

				if (!_stopOrders.TryGetValue(transId, out var info))
				{
					list.RemoveAt(i);
					continue;
				}

				if (info.IsTrailing)
					UpdateTrailing(info, price);

				if (!IsTriggered(info, price))
					continue;

				triggered ??= [];
				triggered.Add(new(info, price, CreateResultingOrder(info, time)));

				_stopOrders.Remove(transId);
				list.RemoveAt(i);
			}

			if (list.Count == 0)
				_bySecurityId.Remove(securityId);

			return triggered ?? (IReadOnlyList<StopOrderTrigger>)[];
		}
	}

	/// <inheritdoc />
	public void Clear()
	{
		using (_sync.EnterScope())
		{
			_stopOrders.Clear();
			_bySecurityId.Clear();
		}
	}

	/// <summary>
	/// Advances a trailing stop's activation price toward the market as new extremes are seen. A
	/// trailing sell tracks the running maximum price (a rising high): the stop is recomputed as
	/// <c>high - offset</c>, or <c>high * (1 - pct / 100)</c> when
	/// <see cref="StopOrderInfo.IsTrailingOffsetPercent"/> is <see langword="true"/>. A trailing buy
	/// tracks the running minimum price (a falling low): the stop is recomputed as <c>low + offset</c>,
	/// or <c>low * (1 + pct / 100)</c> for a percent offset. The extreme is remembered in
	/// <see cref="StopOrderInfo.BestSeenPrice"/>, so the stop only ever moves in the favourable
	/// direction and never back. Applies only when <see cref="StopOrderInfo.IsTrailing"/> is
	/// <see langword="true"/>.
	/// </summary>
	/// <param name="info">The trailing stop whose <see cref="StopOrderInfo.StopPrice"/> is recomputed.</param>
	/// <param name="price">The latest market price for the tick.</param>
	private static void UpdateTrailing(StopOrderInfo info, decimal price)
	{
		var offset = info.TrailingOffset ?? 0;

		if (info.Side == Sides.Sell)
		{
			// Trailing sell: track max price
			if (info.BestSeenPrice is null || price > info.BestSeenPrice)
			{
				info.BestSeenPrice = price;
				info.StopPrice = info.IsTrailingOffsetPercent
					? price * (1 - offset / 100m)
					: price - offset;
			}
		}
		else
		{
			// Trailing buy: track min price
			if (info.BestSeenPrice is null || price < info.BestSeenPrice)
			{
				info.BestSeenPrice = price;
				info.StopPrice = info.IsTrailingOffsetPercent
					? price * (1 + offset / 100m)
					: price + offset;
			}
		}
	}

	/// <summary>
	/// Evaluates whether the current <paramref name="price"/> activates the stop, honouring its side and
	/// mode. In TakeProfit mode (<see cref="StopOrderInfo.InvertTrigger"/> is <see langword="true"/>) a
	/// sell triggers when <c>price &gt;= StopPrice</c> and a buy triggers when <c>price &lt;= StopPrice</c>.
	/// In the default StopLoss mode a buy triggers when <c>price &gt;= StopPrice</c> and a sell triggers
	/// when <c>price &lt;= StopPrice</c>.
	/// </summary>
	/// <param name="info">The stop being evaluated, including its side, mode and stop price.</param>
	/// <param name="price">The latest market price for the tick.</param>
	/// <returns><see langword="true"/> when the stop should fire; otherwise <see langword="false"/>.</returns>
	private static bool IsTriggered(StopOrderInfo info, decimal price)
	{
		if (info.InvertTrigger)
		{
			// TakeProfit: sell triggers when price rises, buy triggers when price falls
			return info.Side == Sides.Sell
				? price >= info.StopPrice
				: price <= info.StopPrice;
		}

		// StopLoss: buy stop triggers when price rises, sell stop triggers when price falls
		return info.Side == Sides.Buy
			? price >= info.StopPrice
			: price <= info.StopPrice;
	}

	/// <summary>
	/// Builds the <see cref="OrderRegisterMessage"/> submitted to the matcher when the stop fires. When
	/// <see cref="StopOrderInfo.LimitPrice"/> is set the result is a limit order — the limit price is
	/// taken as-is, or derived as a percent of the stop price (buy: <c>stop * (1 + pct / 100)</c>, sell:
	/// <c>stop * (1 - pct / 100)</c>) when <see cref="StopOrderInfo.IsLimitPricePercent"/> is
	/// <see langword="true"/>. When no limit price is set the result is a market order. Side, volume and
	/// portfolio are copied straight from the stop.
	/// </summary>
	/// <param name="info">The triggered stop that defines the resulting order.</param>
	/// <param name="time">The trigger time, stamped as the resulting order's local time.</param>
	/// <returns>The <see cref="OrderRegisterMessage"/> to submit to the matcher.</returns>
	private static OrderRegisterMessage CreateResultingOrder(StopOrderInfo info, DateTime time)
	{
		decimal limit = 0;

		if (info.LimitPrice is decimal lp)
		{
			limit = info.IsLimitPricePercent
				? info.StopPrice * (info.Side == Sides.Buy ? 1 + lp / 100m : 1 - lp / 100m)
				: lp;
		}

		return new()
		{
			SecurityId = info.SecurityId,
			Side = info.Side,
			Volume = info.Volume,
			PortfolioName = info.PortfolioName,
			OrderType = info.LimitPrice is not null ? OrderTypes.Limit : OrderTypes.Market,
			Price = limit,
			LocalTime = time,
		};
	}
}
