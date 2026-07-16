namespace StockSharp.MatchingEngine;

/// <summary>
/// A single price level in the emulator order book. Every level mixes two kinds of liquidity:
/// synthesized market depth held in <c>MarketVolume</c> and real registered user orders indexed by
/// <see cref="EmulatorOrder.TransactionId"/>.
/// </summary>
/// <remarks>
/// Only identified user orders are stored as orders: <see cref="AddOrder"/> rejects a default
/// <see cref="EmulatorOrder.TransactionId"/>, so unidentified book depth is instead accumulated into the
/// synthetic <c>MarketVolume</c> bucket by the owning book. The advertised size <c>TotalVolume</c> equals
/// <c>MarketVolume</c> plus the sum of the remaining <see cref="EmulatorOrder.Balance"/> of every registered
/// order. The level reports <c>IsEmpty</c> only when BOTH sources of liquidity are gone — no synthetic
/// market volume and no registered orders — at which point the owning book prunes it.
/// </remarks>
class OrderBookLevelImpl(decimal price)
{
	private readonly Dictionary<long, EmulatorOrder> _ordersByTransId = [];

	public decimal Price { get; } = price;
	public decimal MarketVolume { get; set; }

	public decimal TotalVolume => MarketVolume + _ordersByTransId.Values.Sum(o => o.Balance);
	public int OrderCount => _ordersByTransId.Count;
	public IEnumerable<EmulatorOrder> Orders => _ordersByTransId.Values;

	public void AddOrder(EmulatorOrder order)
	{
		if (order.TransactionId == default)
			throw new ArgumentException("TransactionId cannot be default", nameof(order));

		_ordersByTransId[order.TransactionId] = order;
	}

	public bool RemoveOrder(long transactionId, out EmulatorOrder order)
	{
		return _ordersByTransId.TryGetValue(transactionId, out order) && _ordersByTransId.Remove(transactionId);
	}

	public bool TryGetOrder(long transactionId, out EmulatorOrder order)
	{
		return _ordersByTransId.TryGetValue(transactionId, out order);
	}

	public IEnumerable<EmulatorOrder> GetAllOrders() => [.. _ordersByTransId.Values];

	public bool IsEmpty => MarketVolume <= 0 && _ordersByTransId.Count == 0;
}

/// <summary>
/// In-memory order book for a single instrument, used by the matching engine as the counterparty
/// inventory that <see cref="OrderMatcher"/> consumes during matching via <see cref="ConsumeVolume"/>.
/// </summary>
/// <remarks>
/// Create a new order book bound to a single instrument (<see cref="SecurityId"/>). The book holds two
/// sides: bids are sorted highest price first and asks are sorted lowest price first, so the "best" quote
/// on either side is always the first entry. Each price level aggregates synthesized market depth
/// (<c>MarketVolume</c>) plus the registered user orders resting at that price (keyed by
/// <see cref="EmulatorOrder.TransactionId"/>); a level's total volume is the market volume plus the sum of
/// the user orders' balances. The running side totals exposed as <see cref="TotalBidVolume"/> and
/// <see cref="TotalAskVolume"/> are cached and kept in sync by every mutation, so callers never have to
/// re-scan the book to read aggregate size.
/// </remarks>
public class OrderBook(SecurityId securityId) : IOrderBook
{
	private readonly SortedDictionary<decimal, OrderBookLevelImpl> _bids = new(new BackwardComparer<decimal>());
	private readonly SortedDictionary<decimal, OrderBookLevelImpl> _asks = [];

	private decimal _totalBidVolume;
	private decimal _totalAskVolume;

	/// <inheritdoc />
	public SecurityId SecurityId { get; } = securityId;

	/// <inheritdoc />
	/// <remarks>
	/// The best bid is the highest-priced level, which — because bids are sorted highest first — is the
	/// first entry on the buy side. Returns <see langword="null"/> when the bid side is empty. The reported
	/// volume is the level's aggregated total (synthetic market depth plus resting user orders).
	/// </remarks>
	public (decimal price, decimal volume)? BestBid
	{
		get
		{
			var first = _bids.FirstOrDefault();
			return first.Value is null ? null : (first.Key, first.Value.TotalVolume);
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// The best ask is the lowest-priced level, which — because asks are sorted lowest first — is the
	/// first entry on the sell side. Returns <see langword="null"/> when the ask side is empty. The reported
	/// volume is the level's aggregated total (synthetic market depth plus resting user orders).
	/// </remarks>
	public (decimal price, decimal volume)? BestAsk
	{
		get
		{
			var first = _asks.FirstOrDefault();
			return first.Value is null ? null : (first.Key, first.Value.TotalVolume);
		}
	}

	/// <inheritdoc />
	public decimal TotalBidVolume => _totalBidVolume;

	/// <inheritdoc />
	public decimal TotalAskVolume => _totalAskVolume;

	/// <inheritdoc />
	public int BidLevels => _bids.Count;

	/// <inheritdoc />
	public int AskLevels => _asks.Count;

	/// <summary>
	/// Get worst (lowest) bid level.
	/// </summary>
	/// <remarks>
	/// The worst bid is the level farthest from the market — the last entry on the buy side under the
	/// highest-first ordering. Returns <see langword="null"/> when the bid side is empty. Used when trimming
	/// stale depth from the far end of the book.
	/// </remarks>
	public (decimal price, decimal volume)? GetWorstBid()
	{
		var last = _bids.LastOrDefault();
		return last.Value is null ? null : (last.Key, last.Value.TotalVolume);
	}

	/// <summary>
	/// Get worst (highest) ask level.
	/// </summary>
	/// <remarks>
	/// The worst ask is the level farthest from the market — the last entry on the sell side under the
	/// lowest-first ordering. Returns <see langword="null"/> when the ask side is empty. Used when trimming
	/// stale depth from the far end of the book.
	/// </remarks>
	public (decimal price, decimal volume)? GetWorstAsk()
	{
		var last = _asks.LastOrDefault();
		return last.Value is null ? null : (last.Key, last.Value.TotalVolume);
	}

	private SortedDictionary<decimal, OrderBookLevelImpl> GetQuotes(Sides side)
		=> side == Sides.Buy ? _bids : _asks;

	private OrderBookLevelImpl GetOrCreateLevel(Sides side, decimal price)
	{
		var quotes = GetQuotes(side);

		if (!quotes.TryGetValue(price, out var level))
		{
			level = new OrderBookLevelImpl(price);
			quotes[price] = level;
		}

		return level;
	}

	/// <inheritdoc />
	/// <remarks>
	/// The single business distinction applied here is order identity: an <see cref="EmulatorOrder"/> whose
	/// <see cref="EmulatorOrder.TransactionId"/> is non-default is stored as a real registered user order at
	/// its price level, whereas a default-transaction quote is folded into that level's synthesized
	/// <c>MarketVolume</c> bucket. Either way the affected side's cached total is increased by the order's
	/// <see cref="EmulatorOrder.Balance"/>.
	/// </remarks>
	public void AddQuote(EmulatorOrder order)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));

		var level = GetOrCreateLevel(order.Side, order.Price);

		if (order.TransactionId != default)
		{
			level.AddOrder(order);
		}
		else
		{
			level.MarketVolume += order.Balance;
		}

		AddTotalVolume(order.Side, order.Balance);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Removes a single registered user order identified by its transaction id from a known price level,
	/// decreasing the side total by the removed order's <see cref="EmulatorOrder.Balance"/> and pruning the
	/// level if it becomes empty. Returns <see langword="false"/> when no such level or order exists.
	/// </remarks>
	public bool RemoveQuote(long transactionId, Sides side, decimal price)
	{
		var quotes = GetQuotes(side);

		if (!quotes.TryGetValue(price, out var level))
			return false;

		if (!level.RemoveOrder(transactionId, out var order))
			return false;

		AddTotalVolume(side, -order.Balance);

		if (level.IsEmpty)
			quotes.Remove(price);

		return true;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Sets the synthesized market volume of a price level to an absolute target (not a delta): the level is
	/// created when it does not yet exist and <paramref name="volume"/> is greater than zero, and it is
	/// removed once the update leaves it empty of both synthetic depth and registered user orders. Registered
	/// user orders already resting at the level are left untouched.
	/// </remarks>
	public void UpdateLevel(Sides side, decimal price, decimal volume)
	{
		var quotes = GetQuotes(side);

		if (quotes.TryGetValue(price, out var level))
		{
			var diff = volume - level.MarketVolume;
			level.MarketVolume = volume;
			AddTotalVolume(side, diff);

			if (level.IsEmpty)
				quotes.Remove(price);
		}
		else if (volume > 0)
		{
			level = new OrderBookLevelImpl(price) { MarketVolume = volume };
			quotes[price] = level;
			AddTotalVolume(side, volume);
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Removes an entire price level and returns the registered user orders that were resting there, so the
	/// caller can cancel or otherwise handle them; the side total is reduced by the level's whole aggregated
	/// volume. Returns an empty sequence when the level does not exist.
	/// </remarks>
	public IEnumerable<EmulatorOrder> RemoveLevel(Sides side, decimal price)
	{
		var quotes = GetQuotes(side);

		if (!quotes.TryGetValue(price, out var level))
			return [];

		var orders = level.GetAllOrders();
		var totalVolume = level.TotalVolume;

		quotes.Remove(price);
		AddTotalVolume(side, -totalVolume);

		return orders;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Enumerates the side's levels in book order — best first — projecting each to its aggregated total
	/// volume together with the registered user orders resting at that price.
	/// </remarks>
	public IEnumerable<OrderBookLevel> GetLevels(Sides side)
	{
		var quotes = GetQuotes(side);

		foreach (var kvp in quotes)
		{
			yield return new OrderBookLevel(kvp.Key, kvp.Value.TotalVolume, [.. kvp.Value.Orders]);
		}
	}

	/// <inheritdoc />
	public decimal GetVolumeAtPrice(Sides side, decimal price)
	{
		var quotes = GetQuotes(side);
		return quotes.TryGetValue(price, out var level) ? level.TotalVolume : 0;
	}

	/// <inheritdoc />
	public IEnumerable<EmulatorOrder> GetOrdersAtPrice(Sides side, decimal price)
	{
		var quotes = GetQuotes(side);
		return quotes.TryGetValue(price, out var level) ? level.Orders : [];
	}

	/// <inheritdoc />
	public bool HasLevel(Sides side, decimal price)
	{
		return GetQuotes(side).ContainsKey(price);
	}

	/// <inheritdoc />
	public void Clear()
	{
		_bids.Clear();
		_asks.Clear();
		_totalBidVolume = 0;
		_totalAskVolume = 0;
	}

	/// <inheritdoc />
	public void Clear(Sides side)
	{
		var quotes = GetQuotes(side);
		quotes.Clear();

		if (side == Sides.Buy)
			_totalBidVolume = 0;
		else
			_totalAskVolume = 0;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Replacing the book from an external market snapshot never silently drops a live user order. Before the
	/// existing quotes are cleared, the caller's registered user orders (those flagged
	/// <see cref="EmulatorOrder.IsUserOrder"/>) are captured; the new market quotes are then loaded from the
	/// snapshot and the preserved user orders are re-added on top, each restoring its
	/// <see cref="EmulatorOrder.Balance"/> into the corresponding side total.
	/// </remarks>
	public void SetSnapshot(IEnumerable<QuoteChange> bids, IEnumerable<QuoteChange> asks)
	{
		// Preserve user orders
		var userBidOrders = _bids.Values
			.SelectMany(l => l.Orders.Where(o => o.IsUserOrder))
			.ToList();

		var userAskOrders = _asks.Values
			.SelectMany(l => l.Orders.Where(o => o.IsUserOrder))
			.ToList();

		Clear();

		// Set market quotes from snapshot
		foreach (var bid in bids)
		{
			var level = GetOrCreateLevel(Sides.Buy, bid.Price);
			level.MarketVolume = bid.Volume;
			_totalBidVolume += bid.Volume;
		}

		foreach (var ask in asks)
		{
			var level = GetOrCreateLevel(Sides.Sell, ask.Price);
			level.MarketVolume = ask.Volume;
			_totalAskVolume += ask.Volume;
		}

		// Restore user orders
		foreach (var order in userBidOrders)
		{
			var level = GetOrCreateLevel(Sides.Buy, order.Price);
			level.AddOrder(order);
			_totalBidVolume += order.Balance;
		}

		foreach (var order in userAskOrders)
		{
			var level = GetOrCreateLevel(Sides.Sell, order.Price);
			level.AddOrder(order);
			_totalAskVolume += order.Balance;
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Produces a <see cref="QuoteChangeMessage"/> snapshot of the current book for the bound
	/// <see cref="SecurityId"/>, projecting each side to its per-level aggregated total volume (synthetic
	/// market depth plus resting user orders), ordered best first.
	/// </remarks>
	public QuoteChangeMessage ToMessage(DateTime localTime, DateTime serverTime)
	{
		return new QuoteChangeMessage
		{
			SecurityId = SecurityId,
			LocalTime = localTime,
			ServerTime = serverTime,
			Bids = [.. _bids.Select(kvp => new QuoteChange(kvp.Key, kvp.Value.TotalVolume))],
			Asks = [.. _asks.Select(kvp => new QuoteChange(kvp.Key, kvp.Value.TotalVolume))],
		};
	}

	/// <inheritdoc />
	public decimal GetTotalVolume(Sides side)
		=> side == Sides.Buy ? _totalBidVolume : _totalAskVolume;

	/// <inheritdoc />
	/// <remarks>
	/// The blunt trim: whole levels beyond <paramref name="maxDepth"/> are dropped from the far (worst) end
	/// of the side regardless of what they hold, and every registered order removed with them is returned so
	/// the caller can react. Contrast with <see cref="TrimSynthesizedDepth"/>, which never removes a level
	/// holding user orders.
	/// </remarks>
	public IEnumerable<EmulatorOrder> TrimToDepth(Sides side, int maxDepth)
	{
		var quotes = GetQuotes(side);
		var result = new List<EmulatorOrder>();

		while (quotes.Count > maxDepth)
		{
			var worst = quotes.Last();
			result.AddRange(worst.Value.Orders);
			AddTotalVolume(side, -worst.Value.TotalVolume);
			quotes.Remove(worst.Key);
		}

		return result;
	}

	/// <inheritdoc />
	/// <remarks>
	/// The user-order-safe trim: synthesized depth is capped to <paramref name="maxDepth"/> levels per side
	/// by stripping only the synthetic <c>MarketVolume</c> from the farthest levels. A level that holds
	/// registered user orders is never removed and its user orders are never reordered; trimming stops at the
	/// first such level so liquidity nearer the market is preserved intact.
	/// </remarks>
	public void TrimSynthesizedDepth(Sides side, int maxDepth)
	{
		if (maxDepth < 1)
			return;

		var quotes = GetQuotes(side);

		// Walk the worst (farthest from the market) levels and drop the synthetic part of the book
		// that grew past maxDepth. Levels that hold real registered user orders are never removed:
		// only their synthesized MarketVolume is cleared, and trimming stops at the first such level
		// so it cannot loop forever and cannot reorder/lose user orders nearer the market.
		while (quotes.Count > maxDepth)
		{
			var worst = quotes.Last();
			var level = worst.Value;

			if (level.OrderCount > 0)
			{
				// Strip the synthetic volume but keep the user orders; the level stays in the book.
				if (level.MarketVolume > 0)
				{
					AddTotalVolume(side, -level.MarketVolume);
					level.MarketVolume = 0;
				}

				break;
			}

			AddTotalVolume(side, -level.MarketVolume);
			quotes.Remove(worst.Key);
		}
	}

	private void AddTotalVolume(Sides side, decimal diff)
	{
		if (side == Sides.Buy)
			_totalBidVolume += diff;
		else
			_totalAskVolume += diff;
	}

	/// <summary>
	/// Find and remove order by transaction ID from any level.
	/// </summary>
	/// <remarks>
	/// Complements <see cref="RemoveQuote"/> for the case where the caller knows only the side and the
	/// transaction id but not the price: it scans every level on the side to locate the order, then removes
	/// it, adjusts the side total by its <see cref="EmulatorOrder.Balance"/>, and prunes the level if it
	/// becomes empty.
	/// </remarks>
	public bool TryRemoveOrder(long transactionId, Sides side, out EmulatorOrder order)
	{
		var quotes = GetQuotes(side);

		foreach (var level in quotes.Values)
		{
			if (level.RemoveOrder(transactionId, out order))
			{
				AddTotalVolume(side, -order.Balance);

				if (level.IsEmpty)
					quotes.Remove(level.Price);

				return true;
			}
		}

		order = null;
		return false;
	}

	/// <summary>
	/// Consume volume from best levels (for matching).
	/// </summary>
	/// <param name="side">Side to consume from (opposite to order side).</param>
	/// <param name="maxPrice">Maximum price for buy / minimum for sell.</param>
	/// <param name="volume">Volume to consume.</param>
	/// <returns>Executions (price, volume, affected orders).</returns>
	/// <remarks>
	/// This is the single primitive the matcher uses for both market and limit fills. It walks the levels of
	/// the requested side from best to worst, stopping when the requested <paramref name="volume"/> is filled
	/// or the price limit is reached: for a buy it stops once the ask price exceeds <paramref name="maxPrice"/>,
	/// and for a sell once the bid price falls below <paramref name="maxPrice"/> (a <see langword="null"/>
	/// limit means market-style, with no price cap). Within each level the synthesized <c>MarketVolume</c> is
	/// consumed first and only then are the registered user orders reduced; a user order whose
	/// <see cref="EmulatorOrder.Balance"/> reaches zero is removed, and any level left empty is pruned.
	/// </remarks>
	public IEnumerable<(decimal price, decimal volume, IReadOnlyList<EmulatorOrder> orders)> ConsumeVolume(
		Sides side,
		decimal? maxPrice,
		decimal volume)
	{
		var quotes = GetQuotes(side);
		var remaining = volume;

		// Levels emptied while consuming are collected and removed in the finally below. Doing it there (instead of
		// snapshotting the whole book with quotes.ToArray() on every match) keeps the dictionary unmodified during
		// its own enumeration, yet the removal still runs when the caller abandons the enumerator early - it breaks
		// out of the loop as soon as the order is filled, and disposing the iterator runs the finally.
		List<decimal> toRemove = null;

		try
		{
			foreach (var kvp in quotes)
			{
				if (remaining <= 0)
					break;

				var price = kvp.Key;

				// Check price limit
				if (maxPrice.HasValue)
				{
					if (side == Sides.Sell && price > maxPrice.Value)
						break;
					if (side == Sides.Buy && price < maxPrice.Value)
						break;
				}

				var level = kvp.Value;
				var available = level.TotalVolume;
				var consumed = remaining.Min(available);

				if (consumed <= 0)
					continue;

				var affectedOrders = level.Orders.ToList();

				// Reduce market volume first
				var marketConsumed = consumed.Min(level.MarketVolume);
				level.MarketVolume -= marketConsumed;
				var orderConsumed = consumed - marketConsumed;

				// Then reduce user orders
				foreach (var order in affectedOrders)
				{
					if (orderConsumed <= 0)
						break;

					var orderConsume = orderConsumed.Min(order.Balance);
					order.Balance -= orderConsume;
					orderConsumed -= orderConsume;

					if (order.Balance <= 0)
						level.RemoveOrder(order.TransactionId, out _);
				}

				AddTotalVolume(side, -consumed);
				remaining -= consumed;

				if (level.IsEmpty)
					(toRemove ??= []).Add(price);

				yield return (price, consumed, affectedOrders);
			}
		}
		finally
		{
			if (toRemove != null)
			{
				foreach (var price in toRemove)
					quotes.Remove(price);
			}
		}
	}
}
