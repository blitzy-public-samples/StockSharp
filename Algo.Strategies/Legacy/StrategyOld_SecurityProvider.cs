namespace StockSharp.Algo.Strategies;

// Legacy StrategyOld fragment: the explicit ISecurityProvider / ISecurityMessageProvider surface,
// delegating all security lookup and change notification to the underlying connector.
// Part of the legacy StrategyOld monolith, retained for reference/equivalence only and superseded
// by the modern Strategy engine (see Strategy_SecurityProvider.cs).
partial class StrategyOld
{
	private ISecurityProvider SecurityProvider => SafeGetConnector();

	int ISecurityProvider.Count => SecurityProvider.Count;

	event Action<IEnumerable<Security>> ISecurityProvider.Added
	{
		add => SecurityProvider.Added += value;
		remove => SecurityProvider.Added -= value;
	}

	event Action<IEnumerable<Security>> ISecurityProvider.Removed
	{
		add => SecurityProvider.Removed += value;
		remove => SecurityProvider.Removed -= value;
	}

	event Action ISecurityProvider.Cleared
	{
		add => SecurityProvider.Cleared += value;
		remove => SecurityProvider.Cleared -= value;
	}

	/// <inheritdoc />
	public ValueTask<Security> LookupByIdAsync(SecurityId id, CancellationToken cancellationToken)
		=> SecurityProvider.LookupByIdAsync(id, cancellationToken);

	IAsyncEnumerable<Security> ISecurityProvider.LookupAsync(SecurityLookupMessage criteria)
		=> SecurityProvider.LookupAsync(criteria);

	ValueTask<SecurityMessage> ISecurityMessageProvider.LookupMessageByIdAsync(SecurityId id, CancellationToken cancellationToken)
		=> SecurityProvider.LookupMessageByIdAsync(id, cancellationToken);

	IAsyncEnumerable<SecurityMessage> ISecurityMessageProvider.LookupMessagesAsync(SecurityLookupMessage criteria)
		=> SecurityProvider.LookupMessagesAsync(criteria);
}
