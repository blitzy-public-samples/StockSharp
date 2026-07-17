namespace StockSharp.Algo.Risk;

/// <summary>
/// The interface, describing risks control manager.
/// </summary>
/// <remarks>
/// A risk manager is the composite orchestrator of the pre-trade risk engine. It owns an ordered,
/// change-notifying collection of <see cref="IRiskRule"/> instances (exposed through <see cref="Rules"/>)
/// and, for every inbound message, evaluates each configured rule in turn and returns those that activate,
/// so the caller can apply the configured protective action. Being an <see cref="ILogSource"/> it takes part
/// in the platform logging hierarchy; being <see cref="IPersistable"/> its rule set can be saved to and
/// loaded from settings storage; and it is cloneable through a save/load round-trip that yields an
/// independent copy carrying the same rule configuration. The concrete implementation is
/// <see cref="RiskManager"/>.
/// </remarks>
public interface IRiskManager : ILogSource, IPersistable, ICloneable<IRiskManager>
{
	/// <summary>
	/// Rule list.
	/// </summary>
	/// <remarks>
	/// The live, change-notifying set of configured rules. Additions and removals take effect immediately, and
	/// the declared order is preserved: every rule in the collection is evaluated, in order, for each message
	/// passed to <see cref="ProcessRules"/>.
	/// </remarks>
	INotifyList<IRiskRule> Rules { get; }

	/// <summary>
	/// To reset the state.
	/// </summary>
	/// <remarks>
	/// Resets the runtime state of every rule currently in <see cref="Rules"/> by delegating to each rule's own
	/// <see cref="IRiskRule.Reset"/>. This clears accumulated, stateful tracking (such as rolling counters or
	/// sliding time windows) while retaining the configured rules and their settings.
	/// </remarks>
	void Reset();

	/// <summary>
	/// To process the trade message.
	/// </summary>
	/// <remarks>
	/// Evaluates <paramref name="message"/> against every rule in <see cref="Rules"/> and returns the subset
	/// whose condition was met (each such rule's <see cref="IRiskRule.ProcessMessage"/> returned
	/// <see langword="true"/>). A reset-type message is treated by implementations as a manager-wide reset: the
	/// state of all rules is reset and no activated rules are returned. An empty result therefore means that no
	/// rule's condition was met for the supplied message.
	/// </remarks>
	/// <param name="message">The trade message.</param>
	/// <returns>List of rules, activated by the message.</returns>
	IEnumerable<IRiskRule> ProcessRules(Message message);
}