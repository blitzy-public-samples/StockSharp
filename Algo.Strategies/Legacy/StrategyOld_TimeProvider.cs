namespace StockSharp.Algo.Strategies;

// Legacy fragment: the StrategyOld monolith's ITimeProvider surface - the public
// CurrentTimeChanged event and its connector-driven OnConnectorCurrentTimeChanged raiser.
// StrategyOld is retained for reference/equivalence testing only and is superseded by the
// modern Strategy engine (canonical type summary lives in StrategyOld.cs).
partial class StrategyOld
{
	/// <inheritdoc />
	public event Action<TimeSpan> CurrentTimeChanged;

	private void OnConnectorCurrentTimeChanged(TimeSpan diff)
		=> CurrentTimeChanged?.Invoke(diff);
}