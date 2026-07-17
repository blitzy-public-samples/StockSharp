# StockSharp Risk Engine — Business Rule Catalog

The **risk engine** is StockSharp's pre-trade safety layer. It sits in the message stream and
watches everything flowing to and from the trading venue — order registrations, order
replacements, executions (own trades and order acknowledgements), portfolio and position changes,
errors, and clock ticks — and reacts the moment a configured limit is breached. Each individual
limit is expressed as a **risk rule**; a rule inspects the messages relevant to it and, when its
condition is met, requests one protective **action**: close open positions, stop trading, or
cancel active orders.

This document is a **plain-language, business-requirements-style catalog** of those rules,
reverse-engineered from the engine's source under `Algo/Risk/*.cs`. It is written for a
non-engineer stakeholder: for every rule it states, in words and short formulas, exactly which
value the rule watches, the threshold or time window it compares against, the precise comparison
(including whether the comparison is inclusive or strict), and the action it takes when it fires —
so the behavior can be reviewed and signed off against business intent.

A few properties hold throughout and are worth stating up front:

- **Documentation only.** This catalog *describes* existing behavior; it changes nothing. Where any
  wording here appears to differ from the engine source, the source code under `Algo/Risk/*.cs` is
  authoritative.
- **Every rule shares one shape.** Each rule implements a single evaluation hook,
  `bool ProcessMessage(Message)`, and returns `true` exactly when it activates. Each rule also
  carries an `Action` that is applied when it activates. See [§1](#1-how-every-rule-works-the-universal-pattern).
- **Three possible actions.** A triggered rule requests exactly one of `ClosePositions`,
  `StopTrading`, or `CancelOrders`; these are the only responses. See
  [§2](#2-actions-taken-when-a-rule-fires).
- **Thresholds and directions are precise.** For most value-threshold rules a **zero threshold
  disables the rule**, and the **sign of the threshold selects the comparison direction** (positive
  = upper bound, negative = lower bound). A few rules deliberately differ (strict vs inclusive
  comparisons, counters rather than thresholds); each difference is called out explicitly.
- **Configuration round-trips.** Every rule can save and restore its configured settings (its action
  and thresholds) so a configured rule set can be reproduced exactly. This is ordinary settings
  serialization; there is no external data store of any kind behind the risk engine.

The catalog is organized as follows:

1. [How every rule works (the universal pattern)](#1-how-every-rule-works-the-universal-pattern)
2. [Actions taken when a rule fires](#2-actions-taken-when-a-rule-fires)
3. [The 16 risk rules](#3-the-16-risk-rules)
4. [Worked examples](#4-worked-examples)
5. [Configuration, persistence & lifecycle (shared behavior)](#5-configuration-persistence--lifecycle-shared-behavior)

---

## 1. How every rule works (the universal pattern)

Every risk rule is a small, self-contained check with the **same overall shape**. Understanding the
shape once means every rule in [§3](#3-the-16-risk-rules) can be read as a single sentence.

**The evaluation hook.** Each rule implements one method:

```csharp
bool ProcessMessage(Message message)
```

The risk engine hands each rule every inbound message, one at a time. The rule returns `true` the
moment its own condition is met (the rule *activates* / *triggers*) and `false` otherwise. A rule
also exposes an `Action` property describing what should happen when it activates — one of the three
values in [§2](#2-actions-taken-when-a-rule-fires).

**The five-step shape.** Inside `ProcessMessage`, a typical rule does the following:

1. **Filter on message type.** The rule ignores everything except the message type it cares about
   (for example, a position/money change, an order registration, or an execution report). Any
   other message immediately returns `false`.
2. **Extract a monitored value.** From the accepted message the rule reads the one value it watches
   — a profit-and-loss figure, a commission amount, an order price or volume, a slippage figure, a
   position size, and so on. If the value is missing, the rule returns `false`.
3. **A zero threshold means "disabled."** For the value-threshold rules, a configured threshold of
   **zero** switches the rule off — it never activates. (The counting rules in the Errors and
   frequency families are the exception: they use a positive count rather than a signed threshold.)
4. **The sign of the threshold selects the direction.** A **positive** threshold is treated as an
   **upper bound** (activate when the monitored value has risen to or past it), and a **negative**
   threshold is treated as a **lower bound** (activate when the value has fallen to or past it).
   This one convention lets a single numeric setting express both "too high" and "too low" limits.
5. **A boolean trigger drives the action.** When the comparison is satisfied the rule returns
   `true`, and the engine then enforces the rule's configured `Action`.

**Shared building blocks.** All rules extend a common base (`RiskRule`) that owns the `Action` and a
short human-readable `Title` used for display and logging. Concrete rules add their own configurable
settings (thresholds, windows), can **save and restore** those settings, and — when they keep
running state such as a counter or a seeded baseline — implement a `Reset()` that clears that state
while leaving the configuration untouched. These shared behaviors are summarized in
[§5](#5-configuration-persistence--lifecycle-shared-behavior).

> **Reading tip.** Two small but important variations recur below and are always flagged:
> *inclusive* comparisons (`>=` / `<=`, used by the price, volume, and commission rules) versus the
> *strict* comparison (`>` / `<`) used only by the slippage rule; and *cumulative* counting (a total
> that never resets on success) versus *consecutive* counting (a streak that resets on success).

---


## 2. Actions taken when a rule fires

When a rule activates, it does not act on its own — it *requests* an action, and that action is
enforced downstream on the message flow. There are exactly **three** possible actions, and a
triggered rule requests exactly one of them (the actions do not combine). They are listed here in
their declared order.

### 2.1 The three actions

| # | Action | What it means for the business | How it is enforced |
|---|--------|--------------------------------|--------------------|
| 1 | **ClosePositions** | Flatten and close open positions. | A "close positions" instruction is emitted — an order-group cancel message in *close-positions* mode — directing the venue to flatten open positions. |
| 2 | **StopTrading** | Block new trading until things calm down. | Trading is marked blocked. While blocked, any new **order registration** or **order replacement** is rejected with a failed execution carrying a *"trading disabled"* error, instead of being forwarded. Trading is **automatically unblocked** as soon as a later processed message triggers **no** rules. |
| 3 | **CancelOrders** | Pull the current working orders. | An order-group cancel message is raised (looped back into the risk adapter) that cancels the active orders. |

A rule configured with any value outside these three is a configuration error and is refused at
enforcement time.

### 2.2 How the manager collects triggered rules

The rules are held and evaluated together by the **risk manager**. For each inbound message it works
as follows:

- **On a reset message.** A `Reset` message is treated as a manager-wide reset: every rule's running
  state is cleared (see each rule's `Reset()` in [§5](#5-configuration-persistence--lifecycle-shared-behavior))
  and **no** rules are reported as triggered.
- **With no rules configured.** Nothing is evaluated and **no** rules are reported.
- **Otherwise.** Every configured rule is evaluated against the message **in list order**, and the
  manager returns **all** rules whose `ProcessMessage` returned `true`, preserving that order. The
  caller then enforces each triggered rule's action (as in [§2.1](#21-the-three-actions)).

Because the manager returns *every* triggered rule rather than stopping at the first, a single
message can activate several rules at once, and each of their actions is applied.

---


## 3. The 16 risk rules

There are **16** concrete rules. The table below summarizes all of them at a glance; the sentences
in [§3.2](#32-one-sentence-per-rule) restate each rule in plain language, and the four marked
**(worked example)** are written out in full in [§4](#4-worked-examples).

Throughout, *"triggers when"* describes the exact condition under which the rule returns `true` and
its action fires. Unless noted as **strict**, numeric comparisons are **inclusive** (`>=` / `<=`).

### 3.1 Summary table

| Rule | Monitored message | Extracted value | Threshold / window | Trigger condition | Notes |
|------|-------------------|-----------------|--------------------|-------------------|-------|
| **RiskCommissionRule** | Money `PositionChange` (only when it is a money change) | The currently reported `Commission` value | `Commission` (signed; `0` disables) | `> 0`: `value >= Commission`; `< 0`: `value <= Commission` | Compares the **current reported** value, not a running total. Zero is checked **first** → disabled. |
| **RiskErrorRule** | `Error` | — (counts occurrences) | `Count` (occurrences) | `++count >= Count` | **Cumulative** error counter; never resets on success. `Count` cannot be negative. |
| **RiskOrderCommissionRule** | `Execution` carrying **order** info | Commission on each matching execution | `Commission` (signed; `0` disables) | `> 0`: `total >= Commission`; `< 0`: `total <= Commission` | **Accumulated total** of order-registration commission (see RiskTransactionCommissionRule). |
| **RiskOrderErrorRule** | `Execution` | — (counts consecutive failures) | `Count` (consecutive failures) | `++streak >= Count` | **Consecutive** failures; a successful active-order execution resets the streak to 0. |
| **RiskOrderFreqRule** *(worked example)* | `OrderRegister` / `OrderReplace` | Order count within a time window | `Count` (default **10**) within window `Interval` | `count >= Count` inside the current window | **Sliding window** anchored on the first order's time; window closes and restarts on trigger. Messages with no timestamp are ignored. `Count >= 1`. |
| **RiskOrderPriceRule** | `OrderRegister` / `OrderReplace` | Order `Price` | `Price` | Register: `Price >= threshold`; Replace: `Price > 0` **and** `Price >= threshold` | Inclusive `>=`. |
| **RiskOrderVolumeRule** | `OrderRegister` / `OrderReplace` | Order `Volume` | `Volume` | Register: `Volume >= threshold`; Replace: `Volume > 0` **and** `Volume >= threshold` | Inclusive `>=`. |
| **RiskPnLRule** *(worked example)* | Money `PositionChange` (only when it is a money change) | Current PnL (`CurrentValue`) | `PnL` (a `Unit`, Absolute or Relative; `0` never triggers) | Positive target: `PnL <= current`; negative limit: `PnL >= current` | **First observation seeds a baseline** and never triggers. Relative thresholds offset the baseline. |
| **RiskPositionSizeRule** *(worked example)* | `PositionChange` (**no money filter**) | Position size (`CurrentValue`) | `Position` (signed; `0` disables) | `> 0`: `value >= Position` (long cap); `< 0`: `value <= Position` (short cap) | Watches **position size**, not money; negative thresholds are valid (short cap). |
| **RiskPositionTimeRule** | `PositionChange` and `Time` | Time a position has been open | `Time` (a duration) | Position age `>= Time` | **Stateful**: tracks open time per (security, portfolio); closing the position (size back to 0) drops tracking. |
| **RiskSlippageRule** *(worked example)* | `Execution` | `Slippage` | `Slippage` (signed; `0` disables) | `> 0`: `value > Slippage` (**strict**); `< 0`: `value < Slippage` (**strict**) | **Strict** `>` / `<` — unlike the inclusive `>=` used by the price/volume/commission rules. |
| **RiskTradeCommissionRule** | `Execution` carrying **trade** info | Commission on each matching execution | `Commission` (signed; `0` disables) | `> 0`: `total >= Commission`; `< 0`: `total <= Commission` | **Accumulated total** of own-trade commission (see RiskTransactionCommissionRule). |
| **RiskTradeFreqRule** | `Execution` carrying **trade** info | Trade count within a time window | `Count` (default **10**) within window `Interval` | `count >= Count` inside the current window | **Sliding window**, same mechanics as RiskOrderFreqRule. `Count >= 1`. |
| **RiskTradePriceRule** | `Execution` carrying **trade** info | `TradePrice` | `Price` | `TradePrice >= Price` | Inclusive `>=`. |
| **RiskTradeVolumeRule** | `Execution` carrying **trade** info | `TradeVolume` | `Volume` | `TradeVolume >= Volume` | Inclusive `>=`. |
| **RiskTransactionCommissionRule** | `Execution` | Commission on each matching execution | `Commission` (signed; `0` disables) | `> 0`: `total >= Commission`; `< 0`: `total <= Commission` | **Abstract base** of the two commission-total rules above; accumulates a running total. Subclasses supply the match test. |

---


### 3.2 One sentence per rule

The rules are grouped here by the family they belong to (the grouping used in the configuration UI).

#### PnL & money

- **RiskCommissionRule** — Watching money position changes, this rule fires when the **currently
  reported** commission figure reaches a positive `Commission` limit (`value >= Commission`) or
  falls to a negative one (`value <= Commission`); a limit of `0` disables it, and it compares the
  latest reported value rather than any accumulated total.
- **RiskPnLRule** *(worked example — see [§4.1](#41-riskpnlrule))* — Watching money position
  changes, this rule seeds a PnL baseline on its first observation and thereafter fires when a
  positive profit target is reached (`PnL <= current`) or a negative loss limit is reached
  (`PnL >= current`); a limit of `0` never triggers.
- **RiskTradeCommissionRule** — Accumulates the commission from each own **trade** and fires when
  that running total reaches a positive `Commission` limit (`total >= Commission`) or falls to a
  negative one (`total <= Commission`); a limit of `0` disables it.

#### Positions

- **RiskPositionSizeRule** *(worked example — see [§4.2](#42-riskpositionsizerule))* — Watching
  position-size changes (with **no** money filter), this rule fires when the position grows to a
  positive cap (`value >= Position`, a long cap) or shrinks to a negative cap (`value <= Position`,
  a short cap); a cap of `0` disables it.
- **RiskPositionTimeRule** — Tracks how long each position (per security and portfolio) has been
  open and fires once a position has been held for at least the configured `Time`, evaluated both
  when position changes arrive and when the clock advances; closing a position (size returning to 0)
  stops tracking it.

#### Orders

- **RiskOrderCommissionRule** — Accumulates the commission reported on **order** executions and
  fires when that running total reaches a positive `Commission` limit (`total >= Commission`) or
  falls to a negative one (`total <= Commission`); a limit of `0` disables it.
- **RiskOrderErrorRule** — Counts **consecutive** failed order executions and fires once the streak
  reaches `Count`; a successful execution that reports an active order resets the streak to zero
  (contrast the cumulative RiskErrorRule).
- **RiskOrderFreqRule** *(worked example — see [§4.3](#43-riskorderfreqrule))* — Counts order
  registrations and replacements inside a sliding time window of length `Interval` and fires when
  the count reaches `Count` (default **10**) within that window; the window opens on the first
  order and restarts after each trigger.
- **RiskOrderPriceRule** — Fires when a submitted order's price reaches the `Price` threshold
  (`Price >= threshold`); for a replacement it also requires the new price to be strictly positive.
- **RiskOrderVolumeRule** — Fires when a submitted order's volume reaches the `Volume` threshold
  (`Volume >= threshold`); for a replacement it also requires the new volume to be strictly positive.
- **RiskSlippageRule** *(worked example — see [§4.4](#44-riskslippagerule))* — Reads the slippage on
  an execution and fires when it exceeds a positive limit (**strict** `value > Slippage`) or drops
  below a negative one (**strict** `value < Slippage`); a limit of `0` disables it. This is the one
  rule that uses a **strict** comparison rather than an inclusive one.

#### Trades

- **RiskTradeFreqRule** — Counts own trades inside a sliding time window of length `Interval` and
  fires when the count reaches `Count` (default **10**) within that window, using the same window
  mechanics as RiskOrderFreqRule.
- **RiskTradePriceRule** — Fires when an own trade prints at a price that reaches the `Price`
  threshold (`TradePrice >= Price`, inclusive).
- **RiskTradeVolumeRule** — Fires when an own trade prints with a volume that reaches the `Volume`
  threshold (`TradeVolume >= Volume`, inclusive).

#### Errors

- **RiskErrorRule** — Maintains a **cumulative** count of error notifications and fires once that
  count reaches `Count`; the count is not reset by success, only by an explicit reset (contrast the
  consecutive RiskOrderErrorRule).

#### Commission accumulation base

- **RiskTransactionCommissionRule** — The **abstract base** shared by RiskOrderCommissionRule and
  RiskTradeCommissionRule. It accumulates the commission of each matching execution into a running
  **total** and fires when that total reaches a positive `Commission` limit (`total >= Commission`)
  or falls to a negative one (`total <= Commission`); a limit of `0` disables it. Each subclass
  supplies the test that decides which executions count (order executions for one, own trades for
  the other). This "accumulate a total" family is deliberately distinct from RiskCommissionRule,
  which compares a single currently reported value.

---


## 4. Worked examples

The four rules below are written out in full, step by step, because they showcase the pattern
variations that recur across the whole catalog: signed thresholds, a seeded baseline, absolute vs
relative units, a sliding time window, and the one strict comparison.

### 4.1 RiskPnLRule

**Family:** PnL & money. **Watches:** portfolio profit-and-loss.

1. **Filter.** Only **money** position changes are considered. Any other message, or a non-money
   position change, returns `false`.
2. **Extract.** The current profit-and-loss figure is read from the change's current value. If that
   value is absent, the rule returns `false`.
3. **Seed the baseline.** The **first** qualifying observation only records a baseline (the starting
   PnL) and returns `false`. Nothing can trigger on the very first reading.
4. **Compare.** The configured `PnL` threshold is a `Unit`, which can be **Absolute** or
   **Relative**, and its sign selects the direction:
   - **Absolute** threshold — compared against the current PnL directly:
     - positive `PnL` (a profit target): triggers when `PnL <= current`;
     - negative `PnL` (a loss limit): triggers when `PnL >= current`;
     - `0`: never triggers.
   - **Relative** threshold — the configured offset is added to the seeded baseline first
     (`baseline + PnL`), then the same comparisons apply:
     - positive `PnL`: triggers when `(baseline + PnL) <= current`;
     - negative `PnL`: triggers when `(baseline + PnL) >= current`;
     - `0`: never triggers.
5. **Act.** On a trigger, the rule's configured action is enforced.
6. **Reset.** `Reset()` clears the seeded baseline, so the next observation re-seeds it.

*In business terms:* "Once trading is under way, alert me (and take my configured action) as soon as
my portfolio's profit reaches my target, or my loss reaches my limit — measured either as an
absolute money amount or relative to where PnL stood when monitoring began."

### 4.2 RiskPositionSizeRule

**Family:** Positions. **Watches:** the size of an open position.

1. **Filter.** Only position changes are considered — and, importantly, **there is no money filter**,
   so this rule looks at genuine **position size**, not portfolio money. Any other message returns
   `false`.
2. **Extract.** The current position size is read from the change's current value. If that value is
   absent, the rule returns `false`.
3. **Zero disables.** If the configured `Position` cap is `0`, the rule is disabled and never
   triggers.
4. **Sign selects direction.**
   - positive `Position` (a **long** cap): triggers when `value >= Position`;
   - negative `Position` (a **short** cap): triggers when `value <= Position`.
   Negative caps are valid and are the intended way to bound short exposure.
5. **Act.** On a trigger, the rule's configured action is enforced.

*In business terms:* "Never let my net position exceed this many contracts long (positive cap) or
this many short (negative cap); a cap of zero turns the check off."

### 4.3 RiskOrderFreqRule

**Family:** Orders. **Watches:** how many orders are sent within a rolling time window.

1. **Filter.** Only order **registrations** and **replacements** are counted. Any other message
   returns `false`.
2. **Timestamp guard.** The message's local time is used as the clock. A message with a
   default/zero timestamp is ignored and returns `false`.
3. **Open a window.** When no window is currently open, the arriving order opens one that ends at
   `time + Interval`, sets the running count to `1`, and returns `false`.
4. **Count within the window.** While the arriving order's time is still **inside** the current
   window, the count is incremented. If the count reaches `Count` (**default 10**), the rule
   **triggers** (returns `true`) and the window is **closed** so that counting restarts on the next
   order.
5. **Restart after the window elapses.** If the window has already elapsed, a **fresh** window is
   started (end at `time + Interval`, count reset to `1`) and the rule returns `false`.
6. **Act.** On a trigger, the rule's configured action is enforced.

`Count` must be at least `1`, and `Interval` must be non-negative.

*In business terms:* "Don't let me fire off more than N orders within any Interval-long burst; if I
do, take my configured action and start counting the next burst fresh."

### 4.4 RiskSlippageRule

**Family:** Orders. **Watches:** execution slippage.

1. **Filter.** Only execution messages are considered. Any other message returns `false`.
2. **Extract.** The slippage figure is read from the execution. If slippage is absent, the rule
   returns `false`.
3. **Zero disables.** If the configured `Slippage` limit is `0`, the rule is disabled and never
   triggers.
4. **Sign selects direction — with a strict comparison.**
   - positive `Slippage` (an upper bound): triggers when `value > Slippage`;
   - negative `Slippage` (a lower bound): triggers when `value < Slippage`.

   **Note the strict comparison.** Unlike the price, volume, and commission rules — which use
   *inclusive* `>=` / `<=` — this rule uses **strict** `>` / `<`. Slippage exactly equal to the
   limit does **not** trigger.
5. **Act.** On a trigger, the rule's configured action is enforced.

*In business terms:* "Alert me only when slippage is worse than my limit — strictly beyond it, not
merely at it."

---


## 5. Configuration, persistence & lifecycle (shared behavior)

Beyond its individual trigger logic, every rule shares a small set of behaviors inherited from the
common `RiskRule` base. These are described here once rather than repeated for each rule.

- **The action to take.** Every rule carries an `Action` — one of the three responses in
  [§2](#2-actions-taken-when-a-rule-fires) — which is applied when the rule activates. It is part of
  the shared base, so every rule can request any of the three actions.
- **A display title.** Every rule exposes a short, human-readable `Title` (produced by the rule's
  `GetTitle()`), typically derived from its configured threshold or window. The title is used for
  display and logging; it refreshes automatically whenever the configuration changes.
- **Configurable settings.** Each rule's tunable values — thresholds such as `Commission`, `Price`,
  `Volume`, `Position`, or `Slippage`; counts such as `Count`; durations such as `Interval` or
  `Time`; and the profit-and-loss `PnL` unit — are exposed as display-annotated configuration
  properties so they can be edited and grouped in the settings UI (under groups such as *General*,
  *PnL*, *Positions*, *Orders*, *Trades*, and *Strategy*). Several setters validate their input: the
  price, volume, and error-count settings reject negative values (a negative value would be
  meaningless there), the frequency rules require a count of at least one, and the duration settings
  reject values below zero. By contrast, the commission, position-size, and PnL thresholds
  intentionally **accept** negative values, because for those rules the sign selects the comparison
  direction (see [§1](#1-how-every-rule-works-the-universal-pattern)).
- **Saving and restoring configuration.** Every rule can **save** its configured settings and
  **load** them back, so a configured rule set can be reproduced exactly. This is ordinary
  serialization of the rule's own settings (its action and thresholds); there is **no external data
  store** involved.
- **Resetting running state.** Rules that keep running state implement `Reset()` to clear just that
  state while leaving the configuration intact. In practice:
  - **RiskErrorRule** and **RiskOrderErrorRule** clear their counters (cumulative and consecutive
    respectively);
  - **RiskOrderFreqRule** and **RiskTradeFreqRule** discard the open window and zero the count;
  - **RiskPnLRule** clears its seeded baseline so the next observation re-seeds it;
  - **RiskPositionTimeRule** clears its per-position open-time tracking;
  - the commission-total rules (**RiskOrderCommissionRule**, **RiskTradeCommissionRule**, and their
    base **RiskTransactionCommissionRule**) clear the accumulated total.

  The purely comparative rules (for example the price and volume rules) hold no running state, so
  their reset is a no-op.

Together these shared behaviors mean every rule can be configured, labeled, saved, restored, and
reset uniformly — while the trigger logic catalogued in [§3](#3-the-16-risk-rules) and
[§4](#4-worked-examples) is what makes each rule distinct.

