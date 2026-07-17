namespace StockSharp.Algo.Risk;

/// <summary>
/// Base risk-rule.
/// </summary>
/// <remarks>
/// Serves as the common base type for all concrete risk rules and implements <see cref="IRiskRule"/>.
/// Concrete rules follow a universal pattern: they filter on a specific message type, extract a single
/// monitored value, treat a zero threshold as disabled, use the sign of the threshold to select the
/// comparison direction, and return a boolean trigger that drives the configured <see cref="Action"/>.
/// Each concrete rule adds its own <c>[Display]</c>-attributed configuration properties (thresholds,
/// windows, and so on). This base owns the mutable <see cref="Action"/> and the display <see cref="Title"/>,
/// persists <see cref="Action"/> through <see cref="Load"/> and <see cref="Save"/>, and raises change
/// notifications through <see cref="INotifyPropertyChanged"/> for user-interface binding.
/// </remarks>
public abstract class RiskRule : IRiskRule, INotifyPropertyChanged
{
	/// <summary>
	/// Initialize <see cref="RiskRule"/>.
	/// </summary>
	/// <remarks>
	/// Sets the initial <see cref="Title"/> by calling <see cref="UpdateTitle"/>; derived rules supply the
	/// title text through their <see cref="GetTitle"/> override.
	/// </remarks>
	protected RiskRule()
	{
		UpdateTitle();
	}

	/// <summary>
	/// Get title.
	/// </summary>
	/// <remarks>
	/// Derived rules return a short label reflecting their configured threshold (for example, the numeric
	/// limit). Called whenever the configuration changes to refresh <see cref="Title"/>.
	/// </remarks>
	protected abstract string GetTitle();

	/// <summary>
	/// Update title.
	/// </summary>
	/// <remarks>
	/// Recomputes <see cref="Title"/> from <see cref="GetTitle"/>. Concrete property setters call this after
	/// a value change so the display label stays in sync with the current configuration.
	/// </remarks>
	protected void UpdateTitle() => Title = GetTitle();

	private string _title;

	/// <summary>
	/// Header.
	/// </summary>
	/// <remarks>
	/// Human-readable label for the rule. It is not shown in property grids (<c>[Browsable(false)]</c>); it is
	/// updated through <see cref="UpdateTitle"/> and raises <see cref="INotifyPropertyChanged"/> when it changes.
	/// </remarks>
	[Browsable(false)]
	public string Title
	{
		get => _title;
		private set
		{
			_title = value;
			NotifyChanged();
		}
	}

	private RiskActions _action;

	/// <inheritdoc />
	/// <remarks>
	/// The response enforced when this rule activates, localized for display under the General group.
	/// The setter suppresses redundant assignments and raises a change notification.
	/// </remarks>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ActionKey,
		Description = LocalizedStrings.RiskRuleActionKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public RiskActions Action
	{
		get => _action;
		set
		{
			if (_action == value)
				return;

			_action = value;
			NotifyChanged();
		}
	}

	/// <inheritdoc />
	/// <remarks>
	/// Virtual extension point: the base implementation is a deliberate no-op. Stateful rules override it to
	/// clear counters, sliding windows, or seeded baselines while leaving their configuration intact.
	/// </remarks>
	public virtual void Reset()
	{
	}

	/// <inheritdoc />
	/// <remarks>
	/// Abstract hook implemented by each concrete rule: it evaluates a single message and returns
	/// <see langword="true"/> when the rule's condition is met, otherwise <see langword="false"/>.
	/// </remarks>
	public abstract bool ProcessMessage(Message message);

	/// <inheritdoc />
	/// <remarks>
	/// Restores the <see cref="Action"/> value from the <c>Action</c> key as a <see cref="RiskActions"/>.
	/// Derived rules call the base implementation first and then load their own threshold properties.
	/// </remarks>
	public virtual void Load(SettingsStorage storage)
	{
		Action = storage.GetValue<RiskActions>(nameof(Action));
	}

	/// <inheritdoc />
	/// <remarks>
	/// Persists the <see cref="Action"/> value under the <c>Action</c> key using string conversion.
	/// Derived rules call the base implementation first and then save their own threshold properties.
	/// </remarks>
	public virtual void Save(SettingsStorage storage)
	{
		storage.SetValue(nameof(Action), Action.To<string>());
	}

	private PropertyChangedEventHandler _propertyChanged;

	event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
	{
		add => _propertyChanged += value;
		remove => _propertyChanged -= value;
	}

	/// <remarks>
	/// Raises the property-change notification. When invoked without an explicit property name, the compiler
	/// supplies the calling member's name automatically via <c>[CallerMemberName]</c>.
	/// </remarks>
	private void NotifyChanged([CallerMemberName]string propertyName = null)
	{
		_propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}