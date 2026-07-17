namespace StockSharp.Algo.Risk;

using Ecng.Reflection;

/// <summary>
/// The <see cref="IRiskRule"/> provider.
/// </summary>
/// <remarks>
/// Supplies the set of available risk-rule <see cref="System.Type"/> values - the concrete
/// <see cref="IRiskRule"/> implementations that can be instantiated - so that user interfaces and
/// configuration layers can enumerate them and offer them for selection. This is a marker abstraction
/// that extends <c>ICustomProvider&lt;Type&gt;</c> and declares no additional members of its own; the
/// inherited surface exposes the registered rule types and lets the registry be extended or trimmed.
/// </remarks>
public interface IRiskRuleProvider : ICustomProvider<Type>
{
}

/// <summary>
/// The <see cref="IRiskRule"/> provider.
/// </summary>
/// <remarks>
/// The default in-memory implementation of <see cref="IRiskRuleProvider"/>. On construction it discovers every
/// concrete <see cref="IRiskRule"/> implementation declared in its own assembly by reflection (through
/// <c>FindImplementations</c>), retaining only those types that expose a public parameterless constructor so that
/// each can be created without arguments. The discovered rule types are stored in a thread-safe, cached set.
/// Through the explicitly implemented <c>ICustomProvider&lt;Type&gt;</c> surface, <c>All</c> returns the cached
/// snapshot of registered types, while <c>Add</c> and <c>Remove</c> allow the registry to be adjusted at runtime.
/// </remarks>
public class InMemoryRiskRuleProvider : IRiskRuleProvider
{
	private readonly CachedSynchronizedSet<Type> _all = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="InMemoryRiskRuleProvider"/>.
	/// </summary>
	/// <remarks>
	/// Scans the provider's own assembly for concrete <see cref="IRiskRule"/> implementations and registers only
	/// those that declare a public parameterless constructor, seeding the initial set of available rule types.
	/// </remarks>
	public InMemoryRiskRuleProvider()
		=> _all.AddRange(GetType().Assembly.FindImplementations<IRiskRule>(extraFilter: t => t.GetConstructor(Type.EmptyTypes) != null));

	IEnumerable<Type> ICustomProvider<Type>.All => _all.Cache;

	void ICustomProvider<Type>.Add(Type rule) => _all.Add(rule);
	void ICustomProvider<Type>.Remove(Type rule) => _all.Remove(rule);
}