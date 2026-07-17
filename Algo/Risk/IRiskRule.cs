namespace StockSharp.Algo.Risk;

/// <summary>
/// The interface, describing risk-rule.
/// </summary>
/// <remarks>
/// Concrete rules follow a single, uniform strategy: each rule filters on a specific message type,
/// extracts a monitored value from that message, treats a zero threshold as "disabled", uses the sign
/// of the configured threshold to select the comparison direction (a positive upper bound versus a
/// negative lower bound), and returns a boolean trigger indicating whether the rule activated. When a
/// rule activates, the response it carries in <see cref="Action"/> - one of <see cref="RiskActions"/>
/// (close positions, stop trading, or cancel orders) - is enforced downstream by
/// <see cref="RiskMessageAdapter"/>. Rules are aggregated and evaluated together by
/// <see cref="IRiskManager"/>, and the contract is persistable through the base
/// <see cref="IPersistable"/> so configured thresholds survive save/load round-trips.
/// </remarks>
public interface IRiskRule : IPersistable
{
	/// <summary>
	/// Header.
	/// </summary>
	/// <remarks>
	/// A short, human-readable label for the rule, typically derived from its configured threshold, used for display and logging.
	/// </remarks>
	string Title { get; }

	/// <summary>
	/// Action.
	/// </summary>
	/// <remarks>
	/// The response to enforce when this rule activates; see <see cref="RiskActions"/>.
	/// </remarks>
	RiskActions Action { get; set; }

	/// <summary>
	/// To reset the state.
	/// </summary>
	/// <remarks>
	/// Clears any runtime or accumulated state (counters, sliding windows, seeded baselines) so evaluation restarts cleanly; the configured thresholds are unaffected.
	/// </remarks>
	void Reset();

	/// <summary>
	/// To process the trade message.
	/// </summary>
	/// <remarks>
	/// Evaluates a single inbound <paramref name="message"/> and returns <see langword="true"/> only when the rule's
	/// condition is met on this message; <see cref="IRiskManager"/> collects every rule that returns
	/// <see langword="true"/> and enforces their configured actions.
	/// </remarks>
	/// <param name="message">The trade message.</param>
	/// <returns><see langword="true" />, if the rule is activated, otherwise, <see langword="false" />.</returns>
	bool ProcessMessage(Message message);
}