namespace StockSharp.MatchingEngine;

/// <summary>
/// Emulated portfolio implementation that tracks positions and money in-memory.
/// </summary>
/// <remarks>
/// This is a pure in-memory account model used by the emulator for backtesting and paper trading.
/// It keeps no persistent state: there is no database, file, or external store behind it, so all
/// cash, per-security positions, realized PnL, commission, and funds blocked for working orders live
/// only inside this instance and are discarded on reset. Unrealized PnL is not stored; it is computed on
/// demand by <see cref="CalculateUnrealizedPnL"/> from the in-memory positions and caller-supplied current prices.
/// <para>
/// The account obeys three money identities, expressed here in business terms:
/// current money = starting money + total PnL (<see cref="CurrentMoney"/>);
/// available money = current money - money blocked for working orders (<see cref="AvailableMoney"/>);
/// total PnL = realized PnL - commission paid (<see cref="TotalPnL"/>).
/// </para>
/// <para>
/// Margin thresholds default to a margin-call level of 0.5 (<see cref="MarginCallLevel"/>) and a
/// stop-out level of 0.2 (<see cref="StopOutLevel"/>); automatic stop-out liquidation is disabled by
/// default (<see cref="EnableStopOut"/>).
/// </para>
/// </remarks>
public class EmulatedPortfolio : IPortfolio
{
	private readonly Dictionary<SecurityId, PositionInfo> _positions = [];
	private decimal _beginMoney;
	private decimal _realizedPnL;
	private decimal _totalBlockedMoney;
	private decimal _commission;

	/// <summary>
	/// Initializes a new instance.
	/// </summary>
	/// <param name="name">Portfolio name.</param>
	public EmulatedPortfolio(string name)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
	}

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public decimal BeginMoney => _beginMoney;

	/// <inheritdoc />
	public decimal CurrentMoney => _beginMoney + TotalPnL;

	/// <inheritdoc />
	public decimal AvailableMoney => CurrentMoney - _totalBlockedMoney;

	/// <inheritdoc />
	public decimal RealizedPnL => _realizedPnL;

	/// <inheritdoc />
	public decimal TotalPnL => _realizedPnL - _commission;

	/// <inheritdoc />
	public decimal BlockedMoney => _totalBlockedMoney;

	/// <inheritdoc />
	public decimal Commission => _commission;

	/// <inheritdoc />
	public decimal MarginCallLevel { get; set; } = 0.5m;

	/// <inheritdoc />
	public decimal StopOutLevel { get; set; } = 0.2m;

	/// <inheritdoc />
	public bool EnableStopOut { get; set; }

	/// <inheritdoc />
	public void SetMoney(decimal money)
	{
		_beginMoney = money;
	}

	/// <inheritdoc />
	public void SetPosition(SecurityId securityId, decimal volume, decimal avgPrice = 0)
	{
		var pos = GetOrCreatePosition(securityId);
		pos.BeginValue = volume;
		pos.Diff = 0;
		pos.AveragePrice = avgPrice;
	}

	private PositionInfo GetOrCreatePosition(SecurityId securityId)
	{
		if (!_positions.TryGetValue(securityId, out var pos))
		{
			pos = new PositionInfo(securityId);
			_positions[securityId] = pos;
		}
		return pos;
	}

	/// <inheritdoc />
	public PositionInfo GetPosition(SecurityId securityId)
	{
		return _positions.TryGetValue(securityId, out var pos) ? pos : null;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Applies an executed trade to the account: it adds any commission, updates the position, and
	/// realizes PnL according to how the fill changes that position. When the position is closed out,
	/// PnL is realized for the whole prior position; when a flat position is opened, the trade price
	/// becomes the average entry price; when the position is increased, a new volume-weighted average
	/// entry price is computed; when it is partially closed, PnL is realized only for the closed
	/// portion while the average price is kept; and when it flips from long to short (or vice versa),
	/// the old position is fully realized and a new one is opened at the trade price. Finally, the
	/// bid or ask volume and value that had been reserved for the executed order are released.
	/// </remarks>
	public TradeProcessingResult ProcessTrade(SecurityId securityId, Sides side, decimal price, decimal volume, decimal? commission = null)
	{
		var pos = GetOrCreatePosition(securityId);

		// Update commission
		if (commission.HasValue)
			_commission += commission.Value;

		// Calculate position change
		var positionDelta = side == Sides.Buy ? volume : -volume;
		var prevPos = pos.CurrentValue;
		var prevAvgPrice = pos.AveragePrice;

		pos.Diff += positionDelta;

		var currPos = pos.CurrentValue;
		var tradeRealizedPnL = 0m;

		// Calculate AveragePrice and RealizedPnL
		if (currPos == 0)
		{
			// Position closed completely
			if (prevPos != 0)
			{
				// Realized PnL = (exit price - entry price) * volume * direction
				tradeRealizedPnL = (price - prevAvgPrice) * prevPos.Abs() * Math.Sign(prevPos);
				_realizedPnL += tradeRealizedPnL;
			}
			pos.AveragePrice = 0;
		}
		else if (prevPos == 0)
		{
			// New position opened
			pos.AveragePrice = price;
		}
		else if (Math.Sign(prevPos) == Math.Sign(currPos))
		{
			// Position increased or partially closed
			if (currPos.Abs() > prevPos.Abs())
			{
				// Position increased - recalculate average price
				pos.AveragePrice = (prevAvgPrice * prevPos.Abs() + price * volume) / currPos.Abs();
			}
			else
			{
				// Position partially closed - realize PnL for closed portion
				var closedVolume = prevPos.Abs() - currPos.Abs();
				tradeRealizedPnL = (price - prevAvgPrice) * closedVolume * Math.Sign(prevPos);
				_realizedPnL += tradeRealizedPnL;
				// Average price remains the same for remaining position
			}
		}
		else
		{
			// Position flipped (was long, now short or vice versa)
			// First close old position completely
			tradeRealizedPnL = (price - prevAvgPrice) * prevPos.Abs() * Math.Sign(prevPos);
			_realizedPnL += tradeRealizedPnL;
			// Then open new position at current price
			pos.AveragePrice = price;
		}

		// Update blocked volume/value for active orders (order was executed)
		// Use the average blocked price, not the trade price, to properly unblock
		if (side == Sides.Buy)
		{
			var avgBlockedPrice = pos.TotalBidsVolume > 0 ? pos.TotalBidsValue / pos.TotalBidsVolume : price;
			var blockedValue = volume * avgBlockedPrice;
			pos.TotalBidsVolume -= volume;
			pos.TotalBidsValue -= blockedValue;
		}
		else
		{
			var avgBlockedPrice = pos.TotalAsksVolume > 0 ? pos.TotalAsksValue / pos.TotalAsksVolume : price;
			var blockedValue = volume * avgBlockedPrice;
			pos.TotalAsksVolume -= volume;
			pos.TotalAsksValue -= blockedValue;
		}

		UpdateBlockedMoney();

		return new TradeProcessingResult(tradeRealizedPnL, positionDelta, pos);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Reserves funds for a newly registered working order by adding its volume and notional value
	/// to the position's pending buy or sell totals, then recomputes the money blocked across all
	/// positions so that available money reflects the outstanding order.
	/// </remarks>
	public void ProcessOrderRegistration(SecurityId securityId, Sides side, decimal volume, decimal price)
	{
		var pos = GetOrCreatePosition(securityId);
		var value = volume * price;

		if (side == Sides.Buy)
		{
			pos.TotalBidsVolume += volume;
			pos.TotalBidsValue += value;
		}
		else
		{
			pos.TotalAsksVolume += volume;
			pos.TotalAsksValue += value;
		}

		UpdateBlockedMoney();
	}

	/// <inheritdoc />
	/// <remarks>
	/// Releases funds previously reserved for a working order by subtracting its volume and notional
	/// value from the position's pending buy or sell totals, then recomputes the money blocked across
	/// all positions so that the freed funds become available again.
	/// </remarks>
	public void ProcessOrderCancellation(SecurityId securityId, Sides side, decimal volume, decimal price = 0)
	{
		var pos = GetOrCreatePosition(securityId);
		var value = volume * price;

		if (side == Sides.Buy)
		{
			pos.TotalBidsVolume -= volume;
			pos.TotalBidsValue -= value;
		}
		else
		{
			pos.TotalAsksVolume -= volume;
			pos.TotalAsksValue -= value;
		}

		UpdateBlockedMoney();
	}

	/// <summary>
	/// Recomputes the total money blocked for working orders by netting each position's pending buy
	/// and sell orders against its current exposure, then summing across all positions. For a flat
	/// position the blocked amount is the sum of buy and sell order value; for a long position it is
	/// the greater of (position value plus buy orders) or sell orders; and for a short position it is
	/// the greater of (position value plus sell orders) or buy orders.
	/// </summary>
	private void UpdateBlockedMoney()
	{
		_totalBlockedMoney = 0;
		foreach (var pos in _positions.Values)
		{
			// TotalPrice logic:
			// - If no position: blocked = buys + sells
			// - If long position: blocked = max(position + buys, sells)
			// - If short position: blocked = max(position + sells, buys)
			var positionValue = pos.CurrentValue.Abs() * pos.AveragePrice;
			var buyOrderValue = pos.TotalBidsValue;
			var sellOrderValue = pos.TotalAsksValue;

			decimal blocked;
			if (positionValue == 0)
			{
				blocked = buyOrderValue + sellOrderValue;
			}
			else if (pos.CurrentValue > 0)
			{
				// Long position: max(position + buys, sells)
				blocked = (positionValue + buyOrderValue).Max(sellOrderValue);
			}
			else
			{
				// Short position: max(position + sells, buys)
				blocked = (positionValue + sellOrderValue).Max(buyOrderValue);
			}

			_totalBlockedMoney += blocked;
		}
	}

	/// <inheritdoc />
	public IEnumerable<(SecurityId securityId, decimal volume, decimal avgPrice)> GetPositions()
	{
		return _positions.Select(kvp => (kvp.Key, kvp.Value.CurrentValue, kvp.Value.AveragePrice));
	}

	/// <inheritdoc />
	public IEnumerable<PositionInfo> GetAllPositions() => _positions.Values;

	/// <inheritdoc />
	/// <remarks>
	/// Marks all open positions to market and sums their paper gains and losses: for each position
	/// with a known current price, the unrealized PnL is the difference between the current price and
	/// the average entry price multiplied by the signed position size. Positions whose current price
	/// is unavailable are skipped and contribute nothing.
	/// </remarks>
	public decimal CalculateUnrealizedPnL(Func<SecurityId, decimal?> getCurrentPrice)
	{
		if (getCurrentPrice is null)
			throw new ArgumentNullException(nameof(getCurrentPrice));

		var total = 0m;

		foreach (var pos in _positions.Values)
		{
			if (pos.CurrentValue == 0)
				continue;

			var price = getCurrentPrice(pos.SecurityId);
			if (price is null)
				continue;

			total += (price.Value - pos.AveragePrice) * pos.CurrentValue;
		}

		return total;
	}

	/// <summary>
	/// Clear all state (used by Reset).
	/// </summary>
	internal void Clear()
	{
		_positions.Clear();
		_beginMoney = 0;
		_realizedPnL = 0;
		_totalBlockedMoney = 0;
		_commission = 0;
	}
}

/// <summary>
/// Portfolio manager that creates emulated portfolios in-memory.
/// </summary>
/// <remarks>
/// An in-memory registry of <see cref="EmulatedPortfolio"/> accounts keyed by name, used by the
/// emulator for backtesting and paper trading. Like the portfolios it holds, it keeps no persistent
/// state - there is no database or external store - and it lazily creates one portfolio per name the
/// first time that name is requested. An optional <see cref="IMarginController"/> can be attached to
/// apply leverage-aware fund checks; when none is set, order validation falls back to a plain
/// notional (price times volume) available-funds check.
/// </remarks>
public class EmulatedPortfolioManager : IPortfolioManager
{
	private readonly Dictionary<string, EmulatedPortfolio> _portfolios = [];

	/// <summary>
	/// Margin controller for order validation.
	/// </summary>
	public IMarginController MarginController { get; set; }

	/// <inheritdoc />
	/// <remarks>
	/// Returns the portfolio already registered under the given name, or lazily creates and registers
	/// a new empty <see cref="EmulatedPortfolio"/> for that name on first request.
	/// </remarks>
	public IPortfolio GetPortfolio(string name)
	{
		if (!_portfolios.TryGetValue(name, out var portfolio))
		{
			portfolio = new EmulatedPortfolio(name);
			_portfolios[name] = portfolio;
		}
		return portfolio;
	}

	/// <inheritdoc />
	public bool HasPortfolio(string name)
	{
		return _portfolios.ContainsKey(name);
	}

	/// <inheritdoc />
	public IEnumerable<IPortfolio> GetAllPortfolios()
	{
		return _portfolios.Values;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Checks whether the named portfolio can afford the order; unknown portfolios pass with no error.
	/// When an <see cref="IMarginController"/> is configured, the decision is delegated to it so that
	/// leverage is taken into account; otherwise a plain notional check is applied, rejecting the order
	/// with an "Insufficient funds" error when available money is below price times volume.
	/// </remarks>
	public InvalidOperationException ValidateFunds(string portfolioName, SecurityId securityId, decimal price, decimal volume)
	{
		if (!HasPortfolio(portfolioName))
			return null;

		var portfolio = GetPortfolio(portfolioName);
		var position = portfolio.GetPosition(securityId);

		if (MarginController is not null)
			return MarginController.ValidateOrder(portfolio, price, volume, position);

		var needMoney = price * volume;

		if (portfolio.AvailableMoney < needMoney)
			return new InvalidOperationException($"Insufficient funds: need {needMoney}, available {portfolio.AvailableMoney}");

		return null;
	}

	/// <inheritdoc />
	public void Clear()
	{
		_portfolios.Clear();
	}
}
