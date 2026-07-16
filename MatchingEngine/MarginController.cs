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
/// Portfolio health is measured by a margin level equal to equity ÷ blocked funds. As that level falls it first
/// crosses the <see cref="IPortfolio.MarginCallLevel"/> warning threshold (a margin call) and then, when
/// <see cref="IPortfolio.EnableStopOut"/> is set, the more severe <see cref="IPortfolio.StopOutLevel"/> threshold
/// that forces liquidation of positions (a stop-out).
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
	/// Business rule: a margin call — a warning that the portfolio is approaching under-collateralisation — occurs
	/// when the margin level from <see cref="CheckMarginLevel"/> is &lt;= <see cref="IPortfolio.MarginCallLevel"/>.
	/// </remarks>
	public bool IsMarginCall(IPortfolio portfolio, decimal unrealizedPnL)
		=> CheckMarginLevel(portfolio, unrealizedPnL) <= portfolio.MarginCallLevel;

	/// <inheritdoc />
	/// <remarks>
	/// Business rule: a stop-out — forced liquidation of positions — occurs only when
	/// <see cref="IPortfolio.EnableStopOut"/> is enabled and the margin level from <see cref="CheckMarginLevel"/>
	/// is &lt;= <see cref="IPortfolio.StopOutLevel"/>. This is the more severe threshold beneath the margin-call
	/// warning and automatically closes positions.
	/// </remarks>
	public bool IsStopOut(IPortfolio portfolio, decimal unrealizedPnL)
		=> portfolio.EnableStopOut && CheckMarginLevel(portfolio, unrealizedPnL) <= portfolio.StopOutLevel;
}
