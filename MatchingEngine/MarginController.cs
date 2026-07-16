namespace StockSharp.MatchingEngine;

/// <summary>
/// Default implementation of <see cref="IMarginController"/>.
/// Leverage is taken from <see cref="PositionInfo.Leverage"/> (default 1).
/// Margin call/stop-out levels are taken from <see cref="IPortfolio"/>.
/// </summary>
/// <remarks>
/// Implements the platform's leverage-based margin model, stated here in plain business terms:
/// <para>
/// Required (initial) margin scales with an order's notional value (price × volume) and is reduced
/// proportionally by the position's leverage, which is floored at 1 — so a <see langword="null"/> position or a
/// sub-1 leverage means unleveraged 1:1 trading, and a higher leverage therefore blocks less money.
/// </para>
/// <para>
/// Order acceptance is funds-based: an order is allowed only while the portfolio's free cash
/// (<see cref="IPortfolio.AvailableMoney"/>) covers the required margin, and is otherwise rejected with an
/// "Insufficient funds" error.
/// </para>
/// <para>
/// Portfolio health is measured by a margin level equal to equity ÷ blocked funds. The controller only
/// reports conditions against that level: it reports a margin call when the level is at or below the
/// configurable <see cref="IPortfolio.MarginCallLevel"/>, and reports a stop-out when
/// <see cref="IPortfolio.EnableStopOut"/> is set and the level is at or below the configurable
/// <see cref="IPortfolio.StopOutLevel"/>. Both thresholds are independently configurable on the portfolio; by
/// default the stop-out level sits below the margin-call level, but this class enforces no ordering between them.
/// Acting on a reported condition (issuing a warning, closing positions) is the caller's responsibility; this
/// class performs no liquidation and emits no warning itself.
/// </para>
/// <para>
/// All calculations are pure arithmetic with no persistence, I/O, or other side effects.
/// </para>
/// </remarks>
public class MarginController : IMarginController
{
	/// <inheritdoc />
	/// <remarks>
	/// Business rule: the required (initial) margin equals price × volume ÷ leverage. The leverage is read from
	/// <see cref="PositionInfo.Leverage"/> and defaults to 1 when the position is <see langword="null"/>; it is then
	/// floored at 1, so any missing or sub-1 leverage is treated as unleveraged 1:1 margin. Because leverage divides
	/// the order's notional value, a higher leverage requires proportionally less margin.
	/// </remarks>
	public decimal GetRequiredMargin(decimal price, decimal volume, PositionInfo position)
	{
		var leverage = position?.Leverage ?? 1m;
		if (leverage < 1)
			leverage = 1;

		return price * volume / leverage;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Business rule: the order is accepted only when the portfolio holds enough free cash to cover the required
	/// margin computed by <see cref="GetRequiredMargin"/>. It is rejected — returning an
	/// <see cref="InvalidOperationException"/> whose message begins with "Insufficient funds" — when
	/// <see cref="IPortfolio.AvailableMoney"/> &lt; the required margin; otherwise the method returns
	/// <see langword="null"/> to signal acceptance. A <see langword="null"/> portfolio raises
	/// <see cref="ArgumentNullException"/>.
	/// </remarks>
	public InvalidOperationException ValidateOrder(IPortfolio portfolio, decimal price, decimal volume, PositionInfo position)
	{
		if (portfolio is null)
			throw new ArgumentNullException(nameof(portfolio));

		var needMoney = GetRequiredMargin(price, volume, position);

		if (portfolio.AvailableMoney < needMoney)
			return new InvalidOperationException($"Insufficient funds: need {needMoney}, available {portfolio.AvailableMoney}");

		return null;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Business rule: the margin level is the ratio of portfolio equity to blocked funds. When
	/// <see cref="IPortfolio.BlockedMoney"/> &lt;= 0 there is no open exposure, so the method returns
	/// <see cref="decimal.MaxValue"/> to represent an effectively infinite (fully safe) margin level. Otherwise the
	/// margin level equals (<see cref="IPortfolio.CurrentMoney"/> + unrealizedPnL) ÷ <see cref="IPortfolio.BlockedMoney"/>,
	/// where equity is current money plus the supplied unrealized PnL. A <see langword="null"/> portfolio raises
	/// <see cref="ArgumentNullException"/>.
	/// </remarks>
	public decimal CheckMarginLevel(IPortfolio portfolio, decimal unrealizedPnL)
	{
		if (portfolio is null)
			throw new ArgumentNullException(nameof(portfolio));

		if (portfolio.BlockedMoney <= 0)
			return decimal.MaxValue;

		var equity = portfolio.CurrentMoney + unrealizedPnL;
		return equity / portfolio.BlockedMoney;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Business rule: reports whether a margin-call condition holds — that the portfolio is approaching
	/// under-collateralisation. It returns <see langword="true"/> when the margin level from
	/// <see cref="CheckMarginLevel"/> is &lt;= <see cref="IPortfolio.MarginCallLevel"/>. This method only reports
	/// the condition; it does not itself emit a warning or take any action.
	/// </remarks>
	public bool IsMarginCall(IPortfolio portfolio, decimal unrealizedPnL)
		=> CheckMarginLevel(portfolio, unrealizedPnL) <= portfolio.MarginCallLevel;

	/// <inheritdoc />
	/// <remarks>
	/// Business rule: reports whether a stop-out condition holds. It returns <see langword="true"/> only when
	/// <see cref="IPortfolio.EnableStopOut"/> is enabled and the margin level from <see cref="CheckMarginLevel"/>
	/// is &lt;= <see cref="IPortfolio.StopOutLevel"/>. By default this threshold sits beneath the margin-call
	/// level, but the two are independently configurable and no ordering is enforced. This method only reports the
	/// condition; it does not itself close positions or perform any liquidation — acting on the result is the
	/// caller's responsibility.
	/// </remarks>
	public bool IsStopOut(IPortfolio portfolio, decimal unrealizedPnL)
		=> portfolio.EnableStopOut && CheckMarginLevel(portfolio, unrealizedPnL) <= portfolio.StopOutLevel;
}
