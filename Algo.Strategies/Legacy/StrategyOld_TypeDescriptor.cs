namespace StockSharp.Algo.Strategies;

// ICustomTypeDescriptor fragment of the legacy StrategyOld monolith engine.
//
// This file supplies StrategyOld's component-model type-description surface: it projects the
// strategy's IStrategyParam collection (returned by GetParameters) as PropertyDescriptor instances -
// one nested StrategyParamPropDescriptor per parameter, built on demand by
// ICustomTypeDescriptor.GetProperties - so designers and property grids can discover and bind to
// strategy parameters.
//
// StrategyOld is retained only for reference and equivalence testing and is superseded by the modern
// Strategy engine.
partial class StrategyOld
{
	private class StrategyParamPropDescriptor(IStrategyParam param) : NamedPropertyDescriptor(param.Id, [.. param.Attributes])
	{
		public override Type ComponentType => typeof(StrategyOld);
		public override bool IsReadOnly => false;
		public override Type PropertyType => param.Type;

		public override object GetValue(object component) => param.Value;
		public override void SetValue(object component, object value) => param.Value = value;

		public override bool CanResetValue(object component) => false;
		public override void ResetValue(object component) => throw new NotSupportedException();
		public override bool ShouldSerializeValue(object component) => false;
	}

	/// <summary>
	/// Get parameters.
	/// </summary>
	/// <returns>Parameters.</returns>
	public virtual IStrategyParam[] GetParameters() => Parameters.CachedValues;

	AttributeCollection ICustomTypeDescriptor.GetAttributes() => TypeDescriptor.GetAttributes(this, true);

	string ICustomTypeDescriptor.GetClassName() => TypeDescriptor.GetClassName(this, true);
	string ICustomTypeDescriptor.GetComponentName() => TypeDescriptor.GetComponentName(this, true);
	TypeConverter ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);
	object ICustomTypeDescriptor.GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);
	object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor pd) => this;

	EventDescriptor ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);
	EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);
	EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);

	PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty() => ((ICustomTypeDescriptor)this).GetProperties().TryGetDefault(GetType());
	PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties() => new([.. GetParameters().Select(p => new StrategyParamPropDescriptor(p))]);
	PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[] attributes) => this.GetFilteredProperties(attributes);
}