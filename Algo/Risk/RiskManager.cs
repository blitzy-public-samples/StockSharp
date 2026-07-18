namespace StockSharp.Algo.Risk;

/// <summary>
/// The risks control manager.
/// </summary>
/// <remarks>
/// The default <see cref="IRiskManager"/> implementation and a <see cref="BaseLogReceiver"/>. It owns an
/// observable, thread-safe list of <see cref="IRiskRule"/> instances and, for every inbound message, evaluates
/// each configured rule in list order and returns those that activated, so the caller can enforce their
/// configured protective actions. The rule set is persistable through <see cref="Load"/> and <see cref="Save"/>,
/// and the manager can be duplicated through <see cref="Clone"/>.
/// </remarks>
public class RiskManager : BaseLogReceiver, IRiskManager
{
	/// <summary>
	/// Initializes a new instance of the <see cref="RiskManager"/>.
	/// </summary>
	public RiskManager()
	{
	}

	private readonly CachedSynchronizedList<IRiskRule> _rules = [];

	/// <inheritdoc />
	/// <remarks>
	/// The live, change-notifying collection of configured rules. Additions and removals take effect
	/// immediately, and evaluation order follows list order: each rule is evaluated, in sequence, by
	/// <see cref="ProcessRules"/>.
	/// </remarks>
	public INotifyList<IRiskRule> Rules => _rules;

	/// <inheritdoc />
	/// <remarks>
	/// Resets the runtime state of every configured rule by delegating to each rule's own
	/// <see cref="IRiskRule.Reset"/>, clearing accumulated tracking (such as counter or window state) while
	/// leaving the configured rules and their settings in place.
	/// </remarks>
	public virtual void Reset()
	{
		_rules.Cache.ForEach(r => r.Reset());
	}

	/// <inheritdoc />
	/// <remarks>
	/// Evaluation entry point of the risk engine. A message of type <see cref="MessageTypes.Reset"/> is treated
	/// as a manager-wide reset: every rule's state is reset (see <see cref="Reset"/>) and no activated rules are
	/// returned (an empty sequence). When no rules are configured, an empty sequence is returned as well.
	/// Otherwise the current rule snapshot is evaluated and the method returns every rule whose
	/// <see cref="IRiskRule.ProcessMessage"/> returned <see langword="true"/> for <paramref name="message"/>,
	/// materialized into a list so the caller can enforce each activated rule's action.
	/// </remarks>
	public IEnumerable<IRiskRule> ProcessRules(Message message)
	{
		if (message.Type == MessageTypes.Reset)
		{
			Reset();
			return [];
		}

		var rules = _rules.Cache;

		if (rules.Length == 0)
			return [];

		return [.. rules.Where(r => r.ProcessMessage(message))];
	}

	/// <inheritdoc />
	/// <remarks>
	/// Clears <see cref="Rules"/> and rebuilds it from the stored per-rule settings, restoring each
	/// <see cref="IRiskRule"/> in its entirety (its concrete type together with its own settings) so the rule
	/// set round-trips exactly, then chains to the base <see cref="BaseLogReceiver"/> persistence.
	/// </remarks>
	public override void Load(SettingsStorage storage)
	{
		Rules.Clear();
		Rules.AddRange(storage.GetValue<SettingsStorage[]>(nameof(Rules)).Select(s => s.LoadEntire<IRiskRule>()));

		base.Load(storage);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists every rule in <see cref="Rules"/> by saving each <see cref="IRiskRule"/> in its entirety (its
	/// concrete type together with its own settings), then chains to the base <see cref="BaseLogReceiver"/>
	/// persistence.
	/// </remarks>
	public override void Save(SettingsStorage storage)
	{
		storage.SetValue(nameof(Rules), Rules.Select(r => r.SaveEntire(false)).ToArray());

		base.Save(storage);
	}

	/// <inheritdoc />
	/// <remarks>
	/// Produces an independent copy by round-tripping this manager's configuration through <see cref="Save"/>
	/// into a fresh instance's <see cref="Load"/>, so the clone carries an equivalent but separate rule set.
	/// The non-generic <c>ICloneable.Clone</c> implementation delegates to this typed overload.
	/// </remarks>
	public IRiskManager Clone()
	{
		var clone = new RiskManager();
		clone.Load(this.Save());
		return clone;
	}

	object ICloneable.Clone() => Clone();
}