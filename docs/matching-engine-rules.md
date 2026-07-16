# StockSharp Matching Engine — Business Rule Catalog

The **matching engine** is StockSharp's in-memory, message-driven order matcher and market
emulator. It plays the role of an exchange during backtesting and paper trading: orders, trades,
and market-data updates arrive as messages, and the engine matches incoming orders against a
simulated order book, tracks emulated portfolios and positions, applies a leverage-based margin
model, and triggers stop orders on price movement.

This document is a **plain-language, business-requirements-style catalog** of those rules,
reverse-engineered from the engine's source under `MatchingEngine/*.cs`. It is intended for a
non-engineer stakeholder: it states, in words and short formulas, exactly which condition each
rule checks and what action it takes, so the behavior can be reviewed and validated against
business intent.

A few properties hold throughout and are worth stating up front:

- **In-memory only.** The engine keeps *all* of its state — order books, portfolios, positions,
  realized and unrealized profit-and-loss, blocked funds, and pending stop orders — in memory for
  the lifetime of the emulation. There is **no persistent store** behind it; state is discarded on
  reset. Nothing described here is loaded from or saved to any external system.
- **Documentation only.** This catalog *describes* existing behavior; it changes nothing. Where any
  wording here appears to differ from the engine source, the source code is authoritative.
- **Deterministic and side-effect-free math.** Margin, PnL, and triggering are pure arithmetic over
  the in-memory state, so identical inputs always produce identical outputs.

The rules are organized into four groups:

1. [Order matching](#1-order-matching-ordermatcher--orderbook) — how incoming orders match against the book.
2. [Margin & forced liquidation](#2-margin--forced-liquidation-margincontroller) — how required margin, margin calls, and stop-outs work.
3. [Stop-order triggering](#3-stop-order-triggering-stopordermanager) — how stop and trailing-stop orders arm and fire.
4. [Emulated bookkeeping](#4-emulated-bookkeeping-emulatedportfoliomanager) — how the emulated account tracks money and positions.

---

## 1. Order matching (`OrderMatcher` / `OrderBook`)

The order matcher decides, for each incoming order, how much executes now, at what prices, against
which resting counter-orders, and what happens to any unfilled balance. The result reports the
generated trades, the matched counter-orders (for downstream notification), the remaining volume,
the final order state, whether the order was rejected, and whether any unfilled remainder should
rest in the book.

### 1.1 Order intake and routing

Every incoming order passes through a single entry point, which applies three rules **in this
order**:

1. **Valid inputs required.** A missing order, order book, or settings object is treated as a
   programming error and rejected immediately (an argument error is raised).
2. **Post-only (maker-only) gate.** If the order is flagged **post-only** *and* it **would cross**
   the book (see [§1.5](#15-crossing-test-and-market-price)), it is **rejected outright** with the
   reason **`Post-only order would cross the book`**. The order finishes in state **`Done`**, its
   **entire balance remains unfilled**, and it is **never placed in the book**. This guarantees a
   post-only order can only ever add liquidity, never take it.
3. **Route by order type.** Otherwise a **market** order is routed to market matching
   ([§1.2](#12-market-orders)) and a **limit** order to limit matching
   ([§1.3](#13-limit-orders-and-the-limit-trade-price)).

### 1.2 Market orders

A market order consumes the **opposite** side of the book at **any** available price, taking
resting liquidity from best to worst until the order is filled or the book is exhausted. A market
order **never rests** in the book: any unfilled remainder is simply left unfilled, and the order
**always finishes in state `Done`** regardless of how much was filled.

### 1.3 Limit orders and the limit-trade price

A limit order matches against the opposite side **up to its limit price**.

- If the order's time-in-force is **fill-or-kill** (`MatchOrCancel`), it is handled by the
  fill-or-kill path ([§1.4](#14-fill-or-kill-fok-pre-scan)).
- Otherwise the order consumes whatever crossing volume is available up to its limit price, and the
  unfilled balance is then resolved by time-in-force ([§1.6](#16-time-in-force-tif-mapping)).

**Limit-trade price rule.** The price *printed* on each limit trade depends on the
`UseOrderPriceForLimitTrades` setting:

- when **true** (used for candle-based matching), each trade prints at the **incoming order's own
  limit price**;
- when **false** (the default), each trade prints at the **resting counter-order's price**.

### 1.4 Fill-or-kill (FOK) pre-scan

Fill-or-kill first **pre-scans** the opposite side *without consuming it* to total the available
volume within the limit price:

- for a **buy**, scanning stops once a level's price is **greater than** the limit price;
- for a **sell**, scanning stops once a level's price is **less than** the limit price.

If the pre-scanned available volume is **less than the order balance**, the order is **rejected
with no trades**: the **full balance remains**, nothing rests in the book, and the order finishes
in state **`Done`**. Only when the **entire** quantity can be filled is the actual matching
performed, after which the remaining volume is **`0`** and the final state is **`Done`**.

### 1.5 Crossing test and market price

- **Would-cross test.** A **market** order **always** crosses. For a **limit buy**, it crosses when
  its price is **at or above the best ask**; for a **limit sell**, when its price is **at or below
  the best bid**. If the opposite side is **empty**, the order does **not** cross.
- **Best execution price.** For a **buy** the reference price is the **best ask**; for a **sell** it
  is the **best bid**. When that side is empty there is no price.

### 1.6 Time-in-force (TIF) mapping

An order's time-in-force decides what happens to any balance that cannot be filled immediately.
The engine maps the platform's three time-in-force values to the standard trading terms as follows:

| Time-in-force (`TimeInForce`) | Standard term | Behavior | Rests in book? | Final state |
|-------------------------------|---------------|----------|----------------|-------------|
| `MatchOrCancel` | **FOK** (Fill-Or-Kill) | Fill the order **entirely** now, or reject it with **no trades**. | No | `Done` |
| `CancelBalance` | **IOC** (Immediate-Or-Cancel) | Fill **whatever is available now**; **cancel** the unfilled remainder. | No | `Done` |
| `PutInQueue` (or unset / `null`) | **GTC** (Good-Til-Cancelled) | Fill the crossing part now, then **rest** the remaining balance in the book. | Yes, if any balance remains | `Active` if partially filled, `Done` if fully filled |

### 1.7 Order book mechanics

The order book is an in-memory book for a single instrument, holding the counterparty liquidity the
matcher consumes.

- **Sides and best quote.** **Bids** are sorted **highest price first** and **asks** are sorted
  **lowest price first**, so the **best** quote on each side is always the **first** entry. The best
  bid/ask is empty when that side has no levels.
- **What a level holds.** Each price level combines **synthetic market depth** (a `MarketVolume`
  bucket) **plus** any **registered user orders** resting at that price (keyed by their transaction
  id). A level's advertised size is the market volume **plus the sum of the user orders'
  balances**.
- **Order identity.** An order with a **non-default transaction id** is stored as a **real
  registered user order**; an order with a **default transaction id** is **folded into the synthetic
  market volume** of its level.
- **Snapshot refresh.** Replacing the book from an external market snapshot **never drops a live
  user order**: existing registered user orders are preserved and re-added on top of the freshly
  loaded market quotes.
- **Consuming liquidity.** Matching walks the levels of a side **from best to worst**, stopping when
  the requested volume is filled or the price limit is reached (a buy stops once the ask price
  exceeds the limit; a sell stops once the bid price falls below the limit; no limit means
  market-style with no price cap). Within each level, the **synthetic market volume is consumed
  first** and only then are the **registered user orders** reduced; a fully consumed user order is
  removed and any emptied level is pruned.
- **Depth capping.** Synthesized depth is capped per side by stripping only the **synthetic market
  volume** from the farthest levels. A level that still holds **registered user orders is never
  removed** — only its synthetic volume is cleared — and trimming **stops at the first such level**,
  so liquidity nearer the market and all user orders are preserved.

### 1.8 Matching settings and defaults

The matcher is parameterized by a small settings object. Its business-relevant defaults are:

| Setting | Default | Meaning |
|---------|---------|---------|
| `PriceStep` | `0.01` | Smallest price increment of the instrument. |
| `VolumeStep` | `1` | Smallest volume increment of the instrument. |
| `SpreadSize` | `1` | Spread size, in price steps, when synthesizing a book from ticks. |
| `MaxDepth` | `10` | Maximum synthesized book depth per side. |
| `AllowMarketOrdersWithoutBook` | `true` | Allow market orders to match even without a full book. |
| `UseOrderPriceForLimitTrades` | `false` (unset) | Print limit trades at the order's own price (candle-based matching) rather than the resting price. |

---

## 2. Margin & forced liquidation (`MarginController`)

The margin controller implements the platform's **leverage-based margin model**. Required margin
scales with an order's notional value and shrinks proportionally with leverage; order acceptance is
funds-based; and portfolio health is measured by a **margin level** that, as it falls, first
triggers a **margin call** warning and then, if enabled, a **stop-out** (forced liquidation). All of
its calculations are pure arithmetic with no persistence or other side effects.

The following table states each rule as a `Concept → Rule` pair. In the formulas, *equity* means
current money plus the supplied unrealized PnL.

| Concept | Rule |
|---------|------|
| **Required (initial) margin** | `requiredMargin = price × volume ÷ leverage`. The `leverage` comes from the position (defaulting to `1` when there is no position) and is **floored at `1`** — any missing or below-`1` leverage is treated as unleveraged 1:1 margin. Because leverage divides the notional value, **higher leverage requires proportionally less margin**. |
| **Order validation** | A **null portfolio** is a programming error (an argument error is raised). Otherwise the order is **accepted** (no error returned) as long as the portfolio's free cash covers the required margin. It is **rejected** — returning an error whose message is `Insufficient funds: need {requiredMargin}, available {AvailableMoney}` — when `AvailableMoney < requiredMargin`. |
| **Margin level** | If `BlockedMoney ≤ 0` there is **no open exposure**, so the level is reported as effectively **infinite** (the maximum representable value = fully safe). Otherwise `marginLevel = (CurrentMoney + unrealizedPnL) ÷ BlockedMoney`. |
| **Margin call (warning)** | A margin call — a warning that the portfolio is approaching under-collateralization — occurs when `marginLevel ≤ MarginCallLevel`. |
| **Stop-out (forced liquidation)** | A stop-out — automatic forced liquidation of positions — occurs **only when `EnableStopOut` is `true`** *and* `marginLevel ≤ StopOutLevel`. This is the more severe threshold that sits **below** the margin-call warning level. |

> The portfolio-level thresholds `MarginCallLevel`, `StopOutLevel`, and the `EnableStopOut` switch,
> together with their defaults, are described in [§4](#4-emulated-bookkeeping-emulatedportfoliomanager).


---

## 3. Stop-order triggering (`StopOrderManager`)

The stop-order manager holds pending stop orders (including trailing stops and take-profit stops)
and fires them when the market price reaches their activation level. Each fired stop produces a
resulting order to submit to the matcher.

### 3.1 Indexing

Stop orders are held in two in-memory structures kept in sync: a map **keyed by transaction id**
(the stop itself) and a **per-security index** that maps each `SecurityId` to the list of
transaction ids registered for that instrument. The per-security index lets each incoming price
tick evaluate **only** the stops registered for the affected instrument.

### 3.2 Concurrency invariant (a rule to preserve)

The server drives a single manager instance from two directions at once: the **command path**
(`Register` / `Cancel` / `Replace`) runs on the order-handling thread while the **tick pump**
(`CheckPrice`) runs on the market-data thread. **A single lock serializes all of these
operations.** This single-lock serialization is a **required invariant that must be maintained**:
every mutation path and the price-check path must continue to run under the **same** lock. Without
it, a registration racing a price check could corrupt the per-security index (lost stops or an
out-of-range access), and two concurrent checks could double-trigger the same stop. The lock is
**reentrant**, which is what lets `Replace` hold it across a cancel-then-register so the swap is
atomic (see [§3.4](#34-replace)).

### 3.3 Price checking and one-shot triggering

On each incoming price tick, `CheckPrice(securityId, price, time)`:

1. Looks up the stop list for that instrument (returning an **empty** result when there are none).
2. Iterates the list in **reverse** order. For each stop:
   - if it is a **trailing** stop, its stop price is **updated first** (see
     [§3.5](#35-trailing-stops)); then
   - the **trigger condition** is evaluated (see [§3.6](#36-trigger-direction)).
3. For each stop that triggers, it records a trigger result — pairing the stop with the
   **resulting order** produced for it (see [§3.7](#37-resulting-order)) — and **removes** the stop
   from **both** the transaction-id map and the per-security index. Triggering is therefore
   **one-shot**: a triggered stop fires **exactly once** and is never re-evaluated on a later tick.
4. Returns the collection of stops **newly triggered on this tick** (empty when nothing fired).

### 3.4 Replace

`Replace` is **atomic**: it performs a cancel of the original stop followed by a registration of the
replacement **under the single (reentrant) lock**, so a concurrent price check never observes a
window in which neither the old stop nor its replacement is live.

### 3.5 Trailing stops

A trailing stop follows the market in the favorable direction only, tracking the best price seen so
far and never moving its stop back:

- A **Sell** trailing stop tracks the running **maximum** price (a rising high). When a new high is
  seen, its stop price becomes:
  - `high × (1 − offset ÷ 100)` for a **percent** offset, or
  - `high − offset` for an **absolute** offset.
- A **Buy** trailing stop tracks the running **minimum** price (a falling low). When a new low is
  seen, its stop price becomes:
  - `low × (1 + offset ÷ 100)` for a **percent** offset, or
  - `low + offset` for an **absolute** offset.

### 3.6 Trigger direction

Whether a stop fires depends on its side and whether it is a normal stop-loss or an inverted
take-profit:

| Mode | Buy stop fires when | Sell stop fires when |
|------|---------------------|----------------------|
| **Stop-loss** (normal) | `price ≥ StopPrice` | `price ≤ StopPrice` |
| **Take-profit** (inverted trigger) | `price ≤ StopPrice` | `price ≥ StopPrice` |

### 3.7 Resulting order

When a stop fires, the manager builds the order to submit to the matcher, copying the stop's side,
volume, and portfolio:

- If a **limit price is configured**, the result is a **Limit** order. The limit price is taken
  as-is, or derived as a **percentage of the stop price** when the limit is expressed as a percent
  (`stop × (1 + pct ÷ 100)` for a buy, `stop × (1 − pct ÷ 100)` for a sell).
- If **no limit price is configured**, the result is a **Market** order.


---

## 4. Emulated bookkeeping (`EmulatedPortfolioManager`)

The emulated bookkeeping layer models the trading account during backtesting and paper trading. It
consists of the emulated portfolios themselves and a manager that owns them.

### 4.1 In-memory account model

An emulated portfolio is a **pure in-memory account**. It keeps its per-security positions in an
in-memory dictionary keyed by `SecurityId`, and holds all cash, realized and unrealized PnL,
commission, and funds blocked for working orders **only inside the instance**. There is **no
database, file, or external store** behind it, and all state is discarded on reset.

### 4.2 Money identities

The account obeys three money identities:

```text
CurrentMoney   = BeginMoney + TotalPnL
AvailableMoney = CurrentMoney − BlockedMoney
TotalPnL       = RealizedPnL − Commission
```

In words: **current money** is the starting cash plus total PnL; **available money** is current
money minus the funds blocked for working orders; and **total PnL** is realized PnL minus the
commission paid.

The account's margin thresholds (consumed by [§2](#2-margin--forced-liquidation-margincontroller))
default as follows:

| Threshold | Default | Meaning |
|-----------|---------|---------|
| `MarginCallLevel` | `0.5` | Margin level at or below which a margin-call warning occurs. |
| `StopOutLevel` | `0.2` | Margin level at or below which a stop-out occurs (when enabled). |
| `EnableStopOut` | `false` | Whether automatic stop-out liquidation is active. |

### 4.3 Applying a trade (`ProcessTrade`)

When a trade fills, the account adds any commission, updates the position by the trade's signed
volume, and **realizes PnL according to how the fill changes the position**. The five cases are:

| Case | When | Realized PnL | Average price afterwards |
|------|------|--------------|--------------------------|
| **Closed** | the position returns to flat (was non-zero) | `(price − prevAvgPrice) × abs(prevPosition) × sign(prevPosition)` | reset to `0` |
| **Opened** | was flat, now non-zero | none | set to the **trade price** |
| **Increased** | grows further in the same direction | none | recomputed as a **volume-weighted average**: `(prevAvgPrice × abs(prevPosition) + price × volume) ÷ abs(newPosition)` |
| **Partially closed** | shrinks but stays in the same direction | `(price − prevAvgPrice) × closedVolume × sign(prevPosition)`, where `closedVolume = abs(prevPosition) − abs(newPosition)` | **unchanged** (kept for the remaining position) |
| **Flipped** | direction reverses (long ↔ short) | realize the **whole** old position: `(price − prevAvgPrice) × abs(prevPosition) × sign(prevPosition)` | set to the **trade price** (the newly opened position) |

After realizing PnL, the trade also **unwinds the blocked bid/ask volume and value** that had been
reserved for the now-executed order, using the average blocked price rather than the trade price so
the reservation is released cleanly.

### 4.4 Blocked money (`UpdateBlockedMoney`)

The money blocked for working orders is recomputed per position and **summed across all
positions**. For each position, let `positionValue = abs(position) × averagePrice`, `buys` be the
value of its pending buy orders, and `sells` the value of its pending sell orders:

| Position state | Blocked money for that position |
|----------------|---------------------------------|
| **Flat** (no open position) | `buys + sells` |
| **Long** | `max(positionValue + buys, sells)` |
| **Short** | `max(positionValue + sells, buys)` |

### 4.5 Unrealized PnL (`CalculateUnrealizedPnL`)

Unrealized PnL marks all open positions to market and sums their paper gains and losses:

```text
unrealizedPnL = Σ (currentPrice − averagePrice) × CurrentValue
```

over every open position. A position whose **current price is unavailable is skipped** and
contributes nothing.

### 4.6 The portfolio manager

The manager is an **in-memory registry** of emulated portfolios keyed by name. It keeps no
persistent state and **lazily creates exactly one portfolio per name** the first time that name is
requested. It may optionally hold a **margin controller**.

Its fund-validation rule (`ValidateFunds`) behaves as follows:

- If the named portfolio **does not exist**, validation **passes** (no error) — an unknown portfolio
  is not blocked.
- If a **margin controller is configured**, the decision is **delegated** to it (see
  [§2](#2-margin--forced-liquidation-margincontroller)), so leverage is taken into account.
- Otherwise a **plain notional check** is applied: the order is **rejected** with an
  `Insufficient funds: need {price × volume}, available {AvailableMoney}` error when
  `AvailableMoney < price × volume`, and accepted otherwise.

