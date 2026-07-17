namespace StockSharp.Algo.Risk;

/// <summary>
/// The interface, describing risk-rule.
/// </summary>
/// <remarks>
/// Defines the contract shared by every risk rule. A rule evaluates inbound messages through
/// <see cref="ProcessMessage"/> and returns <see langword="true"/> when its own condition is met
/// (the rule "activates"); it carries the response to take on activation in <see cref="Action"/> -
/// one of <see cref="RiskActions"/> (close positions, stop trading, or cancel orders); it can clear
/// its accumulated runtime state through <see cref="Reset"/>; and it is persistable through the base
/// <see cref="IPersistable"/> so configured thresholds survive save/load round-trips. What a rule
/// monitors is rule-specific rather than universal: the message type it inspects, the value it
/// extracts, whether a zero threshold disables it, and how a threshold sign or time window is
/// interpreted all vary by concrete rule and are documented on that rule. <see cref="IRiskManager"/>
/// aggregates the rules, evaluates them together, and returns those that activated; the carried
/// <see cref="Action"/> is then enforced downstream by <see cref="RiskMessageAdapter"/> (or the
/// calling code), not by the manager itself.
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
	/// Clears any runtime or accumulated state (counters, window/counter state, seeded baselines) so evaluation restarts cleanly; the configured thresholds are unaffected.
	/// </remarks>
	void Reset();

	/// <summary>
	/// To process the trade message.
	/// </summary>
	/// <remarks>
	/// Evaluates a single inbound <paramref name="message"/> and returns <see langword="true"/> only when the rule's
	/// condition is met on this message; <see cref="IRiskManager"/> collects every rule that returns
	/// <see langword="true"/> and returns them to its caller, and each activated rule's configured action is
	/// then enforced downstream by <see cref="RiskMessageAdapter"/> (or the calling code).
	/// </remarks>
	/// <param name="message">The trade message.</param>
	/// <returns><see langword="true" />, if the rule is activated, otherwise, <see langword="false" />.</returns>
	bool ProcessMessage(Message message);
}