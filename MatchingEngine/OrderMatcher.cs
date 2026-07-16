namespace StockSharp.MatchingEngine;

/// <summary>
/// Order matcher implementation. Matches an incoming <see cref="EmulatorOrder"/> against the
/// opposite side of an <see cref="IOrderBook"/> and produces a <see cref="MatchResult"/>.
/// </summary>
/// <remarks>
/// This is the core business component of the emulated exchange. For each incoming order it decides
/// how much executes now, at what prices, against which resting counter-orders, and what happens to
/// any unfilled balance. The returned <see cref="MatchResult"/> captures the full outcome: the
/// generated <see cref="MatchTrade"/> list, the matched counter-orders (surfaced for downstream
/// notification), the remaining volume, the final <see cref="OrderStates"/>, whether the order was
/// rejected, and whether the unfilled remainder should rest in the book (see
/// <see cref="MatchResult.ShouldPlaceInBook"/>).
/// <para>
/// Three order-handling rules are applied, in order: a post-only (maker-only) order that would cross
/// the book is rejected outright and never executed; a market order is matched against the opposite
/// side at any price; a limit order is matched up to its limit price subject to its time-in-force.
/// </para>
/// </remarks>
public class OrderMatcher : IOrderMatcher
{
	/// <inheritdoc />
	/// <remarks>
	/// Applies the matcher's three business rules in the following order:
	/// <list type="number">
	/// <item>
	/// <description>
	/// Post-only (maker-only) rule: an order whose <see cref="EmulatorOrder.PostOnly"/> flag is set and
	/// that would cross the book (as determined by <see cref="WouldCross"/>) is rejected with the reason
	/// "Post-only order would cross the book" and is never executed. This guarantees a post-only order can
	/// only add liquidity, never take it.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// Market orders (<see cref="OrderTypes.Market"/>) are routed to marketable matching, which consumes
	/// the opposite side of the book at any available price.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// Limit orders are routed to limit matching, which respects the order's limit price and its
	/// time-in-force.
	/// </description>
	/// </item>
	/// </list>
	/// </remarks>
	public MatchResult Match(EmulatorOrder order, IOrderBook book, MatchingSettings settings)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		if (book is null)
			throw new ArgumentNullException(nameof(book));
		if (settings is null)
			throw new ArgumentNullException(nameof(settings));

		// Post-only check: reject if would cross
		if (order.PostOnly && WouldCross(order, book))
		{
			return new MatchResult
			{
				Order = order,
				IsRejected = true,
				RejectionReason = "Post-only order would cross the book",
				FinalState = OrderStates.Done,
				RemainingVolume = order.Balance,
				ShouldPlaceInBook = false,
			};
		}

		var trades = new List<MatchTrade>();
		var matchedOrders = new List<EmulatorOrder>();

		// Market orders
		if (order.OrderType == OrderTypes.Market)
		{
			return MatchMarketOrder(order, book, settings, trades, matchedOrders);
		}

		// Limit orders
		return MatchLimitOrder(order, book, settings, trades, matchedOrders);
	}

	/// <summary>
	/// Fills a market order against the opposite side of the book at any available price, consuming
	/// resting liquidity until the order is filled or the book is exhausted. Any unfilled remainder is
	/// discarded — a market order never rests in the order book
	/// (<see cref="MatchResult.ShouldPlaceInBook"/> is always <see langword="false"/>), and its final
	/// state is <see cref="OrderStates.Done"/> regardless of how much was filled.
	/// </summary>
	private static MatchResult MatchMarketOrder(
		EmulatorOrder order,
		IOrderBook book,
		MatchingSettings settings,
		List<MatchTrade> trades,
		List<EmulatorOrder> matchedOrders)
	{
		var oppositeSide = order.Side.Invert();
		var remaining = order.Balance;

		// Consume from opposite side at any price
		foreach (var (price, volume, orders) in ((OrderBook)book).ConsumeVolume(oppositeSide, null, remaining))
		{
			var consumed = volume.Min(remaining);
			trades.Add(new MatchTrade(price, consumed, order.Side, orders));
			matchedOrders.AddRange(orders.Where(o => o.IsUserOrder));
			remaining -= consumed;

			if (remaining <= 0)
				break;
		}

		var hasExecution = trades.Count > 0;

		return new MatchResult
		{
			Order = order,
			Trades = trades,
			MatchedOrders = matchedOrders,
			RemainingVolume = remaining,
			ShouldPlaceInBook = false, // Market orders never go to book
			FinalState = OrderStates.Done,
		};
	}

	/// <summary>
	/// Fills a limit order against the opposite side of the book up to the order's limit price, then
	/// resolves the unfilled balance according to the order's time-in-force (TIF).
	/// </summary>
	/// <remarks>
	/// The time-in-force determines what happens to any balance that cannot be filled immediately:
	/// <list type="bullet">
	/// <item>
	/// <description>
	/// <see cref="TimeInForce.MatchOrCancel"/> maps to FOK (Fill-Or-Kill): the whole order must fill
	/// immediately or it is cancelled in its entirety. This case is delegated to <see cref="MatchFOK"/>.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// <see cref="TimeInForce.CancelBalance"/> maps to IOC (Immediate-Or-Cancel): whatever is immediately
	/// available is filled and the unfilled balance is cancelled. The final state is
	/// <see cref="OrderStates.Done"/> and nothing rests in the book.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// <see cref="TimeInForce.PutInQueue"/> (or <see langword="null"/>) maps to GTC (Good-Till-Cancelled):
	/// everything that crosses now is filled and the remaining balance is placed into the book. The order
	/// is left <see cref="OrderStates.Active"/> when only partially filled, or <see cref="OrderStates.Done"/>
	/// when fully filled.
	/// </description>
	/// </item>
	/// </list>
	/// <para>
	/// Limit-trade price rule: when <see cref="MatchingSettings.UseOrderPriceForLimitTrades"/> is
	/// <see langword="true"/> each trade prints at the incoming order's own limit price (used for
	/// candle-based matching); otherwise it prints at the resting counter-order's price.
	/// </para>
	/// </remarks>
	private static MatchResult MatchLimitOrder(
		EmulatorOrder order,
		IOrderBook book,
		MatchingSettings settings,
		List<MatchTrade> trades,
		List<EmulatorOrder> matchedOrders)
	{
		var oppositeSide = order.Side.Invert();
		var remaining = order.Balance;
		var limitPrice = order.Price;

		if (order.TimeInForce == TimeInForce.MatchOrCancel)
			return MatchFOK(order, book, settings);

		// Get matchable volume
		foreach (var (price, volume, orders) in ((OrderBook)book).ConsumeVolume(oppositeSide, limitPrice, remaining))
		{
			var consumed = volume.Min(remaining);
			// Use order price for candle-based matching
			var tradePrice = settings.UseOrderPriceForLimitTrades ? limitPrice : price;
			trades.Add(new MatchTrade(tradePrice, consumed, order.Side, orders));
			matchedOrders.AddRange(orders.Where(o => o.IsUserOrder));
			remaining -= consumed;

			if (remaining <= 0)
				break;
		}

		var hasExecution = trades.Count > 0;
		var isFullyMatched = remaining <= 0;

		// Determine final state based on TimeInForce
		var shouldPlaceInBook = false;
		var finalState = OrderStates.Active;

		switch (order.TimeInForce)
		{
			case TimeInForce.PutInQueue:
			case null:
				// Place remaining in book
				shouldPlaceInBook = remaining > 0;
				finalState = isFullyMatched ? OrderStates.Done : OrderStates.Active;
				break;

			case TimeInForce.MatchOrCancel: // FOK
				finalState = OrderStates.Done;
				break;

			case TimeInForce.CancelBalance: // IOC
				// Execute what we can, cancel the rest
				finalState = OrderStates.Done;
				break;
		}

		return new MatchResult
		{
			Order = order,
			Trades = trades,
			MatchedOrders = matchedOrders,
			RemainingVolume = remaining,
			ShouldPlaceInBook = shouldPlaceInBook,
			FinalState = finalState,
		};
	}

	/// <summary>
	/// Performs the Fill-Or-Kill (FOK) check for a limit order. Pre-scans the opposite side's liquidity
	/// within the limit price without consuming it; if the entire order balance cannot be filled, the
	/// order is rejected with no trades (returned as <see cref="OrderStates.Done"/> with the full balance
	/// remaining). Only when the whole quantity can be filled is the actual matching performed.
	/// </summary>
	private static MatchResult MatchFOK(EmulatorOrder order, IOrderBook book, MatchingSettings settings)
	{
		var oppositeSide = order.Side.Invert();
		var limitPrice = order.Price;

		// Check if we can fill entire order without actually consuming
		var availableVolume = 0m;
		foreach (var level in book.GetLevels(oppositeSide))
		{
			// Check price
			if (order.Side == Sides.Buy && level.Price > limitPrice)
				break;
			if (order.Side == Sides.Sell && level.Price < limitPrice)
				break;

			availableVolume += level.Volume;
			if (availableVolume >= order.Balance)
				break;
		}

		if (availableVolume < order.Balance)
		{
			// Cannot fill entirely - reject (but order is "done" with full balance remaining)
			return new MatchResult
			{
				Order = order,
				Trades = [],
				MatchedOrders = [],
				RemainingVolume = order.Balance,
				ShouldPlaceInBook = false,
				FinalState = OrderStates.Done,
			};
		}

		// Can fill - do actual matching
		var trades = new List<MatchTrade>();
		var matchedOrders = new List<EmulatorOrder>();
		var remaining = order.Balance;

		foreach (var (price, volume, orders) in ((OrderBook)book).ConsumeVolume(oppositeSide, limitPrice, remaining))
		{
			var consumed = volume.Min(remaining);
			var tradePrice = settings.UseOrderPriceForLimitTrades ? limitPrice : price;
			trades.Add(new MatchTrade(tradePrice, consumed, order.Side, orders));
			matchedOrders.AddRange(orders.Where(o => o.IsUserOrder));
			remaining -= consumed;

			if (remaining <= 0)
				break;
		}

		return new MatchResult
		{
			Order = order,
			Trades = trades,
			MatchedOrders = matchedOrders,
			RemainingVolume = 0,
			ShouldPlaceInBook = false,
			FinalState = OrderStates.Done,
		};
	}

	/// <inheritdoc />
	/// <remarks>
	/// This is the crossing test used by the post-only gate in <see cref="Match"/>. A market order always
	/// crosses. For a limit order the opposite best price is taken from the book (best ask for a buy, best
	/// bid for a sell): a buy crosses when its price is at or above the best ask, and a sell crosses when
	/// its price is at or below the best bid. When the opposite side is empty there is no price to cross,
	/// so the order does not cross.
	/// </remarks>
	public bool WouldCross(EmulatorOrder order, IOrderBook book)
	{
		if (order.OrderType == OrderTypes.Market)
			return true;

		var oppositeBest = order.Side == Sides.Buy ? book.BestAsk : book.BestBid;

		if (oppositeBest is null)
			return false;

		if (order.Side == Sides.Buy)
			return order.Price >= oppositeBest.Value.price;
		else
			return order.Price <= oppositeBest.Value.price;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Returns the opposite side's best price — the best ask for a <see cref="Sides.Buy"/> order and the
	/// best bid for a <see cref="Sides.Sell"/> order — or <see langword="null"/> when that side of the
	/// book is empty.
	/// </remarks>
	public decimal? GetMarketPrice(Sides side, IOrderBook book)
	{
		// For buy orders, get best ask; for sell orders, get best bid
		var oppositeBest = side == Sides.Buy ? book.BestAsk : book.BestBid;
		return oppositeBest?.price;
	}
}
