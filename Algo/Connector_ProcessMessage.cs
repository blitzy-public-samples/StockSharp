namespace StockSharp.Algo;

using StockSharp.Algo.Risk;

partial class Connector
{
	private readonly Lock _marketTimerSync = new();
	private ControllablePeriodicTimer _marketTimer;
	private readonly TimeMessage _marketTimeMessage = new();
	private bool _isMarketTimeHandled;

	private void CreateTimer()
	{
		using (_marketTimerSync.EnterScope())
		{
			_isMarketTimeHandled = true;

			if (_marketTimer != null)
				return;

			_marketTimer = AsyncHelper
				.CreatePeriodicTimer(async () =>
				{
					try
					{
						// TimeMsg required for notify invoke CurrentTimeChanged event (and active time based IMarketRule-s)
						// No need to put _marketTimeMessage again, if it still in queue.

						using (_marketTimerSync.EnterScope())
						{
							if (_marketTimer == null || !_isMarketTimeHandled)
								return;

							_isMarketTimeHandled = false;
						}

						_marketTimeMessage.LocalTime = CurrentTime;
						await SendOutMessageAsync(_marketTimeMessage, default);
					}
					catch (Exception ex)
					{
						LogError(ex);
					}
				})
				.Start(MarketTimeChangedInterval);
		}
	}

	private void CloseTimer()
	{
		using (_marketTimerSync.EnterScope())
		{
			_marketTimer?.Dispose();
			_marketTimer = null;

			_isMarketTimeHandled = false;
		}
	}

	/// <inheritdoc />
	public bool CanConnect => Adapter.InnerAdapters.SortedAdapters.Any();

	private readonly ResetMessage _disposeMessage = new();

	private ValueTask AdapterOnNewOutMessage(Message message, CancellationToken cancellationToken)
	{
		if (message.IsBack())
		{
			//message.IsBack = false;

			// lookup messages now sends in BasketMessageAdapter
			// nested subscription ignores by Connector
			//
			//if (message.Type == MessageTypes.MarketData)
			//{
			//	var mdMsg = (MarketDataMessage)message;

			//	var security = !mdMsg.DataType.IsSecurityRequired() ? null : GetSecurity(mdMsg.SecurityId);
			//	_subscriptionManager.ProcessRequest(security, mdMsg, true);
			//}
			//else

			if (message.Type == MessageTypes.OrderGroupCancel)
			{
				var cancelMsg = (OrderGroupCancelMessage)message;
				// offline (back) and risk managers can generate the message
				_entityCache.TryAddMassCancelationId(cancelMsg.TransactionId);
			}
			
			return SendInMessageAsync(message, cancellationToken);
		}

		return SendOutMessageAsync(message, cancellationToken);
	}

	private IMessageChannel _inMessageChannel;

	/// <summary>
	/// Input message channel.
	/// </summary>
	public IMessageChannel InMessageChannel
	{
		get => _inMessageChannel;
		protected set
		{
			if (value == _inMessageChannel)
				return;

			if (_inMessageChannel != null)
			{
				_inMessageChannel.NewOutMessageAsync -= InMessageChannelOnNewOutMessage;
				_inMessageChannel.Dispose();
			}

			_inMessageChannel = value;

			if (_inMessageChannel != null)
				_inMessageChannel.NewOutMessageAsync += InMessageChannelOnNewOutMessage;
		}
	}

	private IMessageChannel _outMessageChannel;

	/// <summary>
	/// Outgoing message channel.
	/// </summary>
	public IMessageChannel OutMessageChannel
	{
		get => _outMessageChannel;
		protected set
		{
			if (value == _outMessageChannel)
				return;

			if (_outMessageChannel != null)
			{
				_outMessageChannel.NewOutMessageAsync -= OutMessageChannelOnNewOutMessage;
				_outMessageChannel.Dispose();
			}

			_outMessageChannel = value;

			if (_outMessageChannel != null)
				_outMessageChannel.NewOutMessageAsync += OutMessageChannelOnNewOutMessage;
		}
	}

	private async ValueTask InMessageChannelOnNewOutMessage(Message message, CancellationToken cancellationToken)
	{
		await (_inAdapter?.SendInMessageAsync(message, cancellationToken) ?? default);

		if (message != _disposeMessage)
			return;

		InMessageChannel = null;
		Adapter = null;
		OutMessageChannel = null;
	}

	private ValueTask OutMessageChannelOnNewOutMessage(Message message, CancellationToken cancellationToken)
	{
		return OnProcessMessage(message, cancellationToken);
	}

	private IMessageAdapterWrapper _inAdapter;

	/// <summary>
	/// Inner message adapter.
	/// </summary>
	public IMessageAdapterWrapper InnerAdapter
	{
		get => _inAdapter;
		set
		{
			if (_inAdapter == value)
				return;

			if (_inAdapter != null)
			{
				_inAdapter.NewOutMessageAsync -= AdapterOnNewOutMessage;
			}

			if (_adapter != null)
			{
				_adapter.InnerAdapters.Added -= InnerAdaptersOnAdded;
				_adapter.InnerAdapters.Removed -= InnerAdaptersOnRemoved;
				_adapter.InnerAdapters.Cleared -= InnerAdaptersOnCleared;
			}

			_inAdapter = value;
			_adapter = null;
			StorageAdapter = null;

			if (_inAdapter == null)
				return;

			var adapter = _inAdapter as IMessageAdapterWrapper;

			while (adapter != null)
			{
				if (adapter is StorageMetaInfoMessageAdapter storage)
					StorageAdapter = storage;

				if (adapter.InnerAdapter is BasketMessageAdapter basket)
					_adapter = basket;

				adapter = adapter.InnerAdapter as IMessageAdapterWrapper;
			}

			if (_adapter != null)
			{
				_adapter.InnerAdapters.Added += InnerAdaptersOnAdded;
				_adapter.InnerAdapters.Removed += InnerAdaptersOnRemoved;
				_adapter.InnerAdapters.Cleared += InnerAdaptersOnCleared;

				foreach (var inner in _adapter.InnerAdapters)
					InnerAdaptersOnAdded(inner);
			}

			_inAdapter.NewOutMessageAsync += AdapterOnNewOutMessage;
		}
	}

	private BasketMessageAdapter _adapter;

	/// <summary>
	/// Message adapter.
	/// </summary>
	public BasketMessageAdapter Adapter
	{
		get => _adapter;
		protected set
		{
			if (!_isDisposing && value == null)
				throw new ArgumentNullException(nameof(value));

			if (_adapter == value)
				return;

			if (_adapter != null)
			{
				_adapter.InnerAdapters.Added -= InnerAdaptersOnAdded;
				_adapter.InnerAdapters.Removed -= InnerAdaptersOnRemoved;
				_adapter.InnerAdapters.Cleared -= InnerAdaptersOnCleared;

				//SendInMessage(new ResetMessage());

				_inAdapter.NewOutMessageAsync -= AdapterOnNewOutMessage;
				_inAdapter.Dispose();

				//if (_inAdapter != _adapter)
				//	_adapter.Dispose();
			}

			_adapter = value;
			_inAdapter = _adapter;

			if (_adapter != null)
			{
				_subscriptionManager.TransactionIdGenerator = _adapter.TransactionIdGenerator;

				_adapter.InnerAdapters.Added += InnerAdaptersOnAdded;
				_adapter.InnerAdapters.Removed += InnerAdaptersOnRemoved;
				_adapter.InnerAdapters.Cleared += InnerAdaptersOnCleared;

				_adapter.Parent = this;

				//_inAdapter = new ChannelMessageAdapter(_inAdapter, InMessageChannel, OutMessageChannel)
				//{
				//	//OwnOutputChannel = true,
				//	OwnInnerAdapter = true
				//};

				if (RiskManager != null)
					_inAdapter = new RiskMessageAdapter(_inAdapter, RiskManager) { OwnInnerAdapter = true };

				if (SecurityStorage != null && StorageRegistry != null)
				{
					_inAdapter = StorageAdapter = new StorageMetaInfoMessageAdapter(_inAdapter, SecurityStorage, PositionStorage, StorageRegistry.ExchangeInfoProvider, _adapter.StorageProcessor)
					{
						OwnInnerAdapter = true,
						OverrideSecurityData = OverrideSecurityData
					};
				}

				if (Buffer != null)
					_inAdapter = new BufferMessageAdapter(_inAdapter, _adapter.StorageSettings, Buffer, SnapshotRegistry);

				if (SupportBasketSecurities)
					_inAdapter = new BasketSecurityMessageAdapter(_inAdapter, this, BasketSecurityProcessorProvider, ExchangeInfoProvider) { OwnInnerAdapter = true };

				if (SupportSnapshots)
					_inAdapter = new SnapshotHolderMessageAdapter(_inAdapter, _entityCache) { OwnInnerAdapter = true };

				if (SupportAssociatedSecurity)
					_inAdapter = new AssociatedSecurityAdapter(_inAdapter) { OwnInnerAdapter = true };

				if (SupportFilteredMarketDepth)
					_inAdapter = new FilteredMarketDepthAdapter(_inAdapter) { OwnInnerAdapter = true };

				_inAdapter.NewOutMessageAsync += AdapterOnNewOutMessage;
			}
		}
	}

	private bool _supportBasketSecurities;

	/// <summary>
	/// Use <see cref="BasketSecurityMessageAdapter"/>.
	/// </summary>
	public bool SupportBasketSecurities
	{
		get => _supportBasketSecurities;
		set
		{
			if (_supportBasketSecurities == value)
				return;

			if (value)
				EnableAdapter(a => new BasketSecurityMessageAdapter(a, this, BasketSecurityProcessorProvider, ExchangeInfoProvider) { OwnInnerAdapter = true }, typeof(BufferMessageAdapter));
			else
				DisableAdapter<BasketSecurityMessageAdapter>();

			_supportBasketSecurities = value;
		}
	}

	private bool _supportFilteredMarketDepth;

	/// <summary>
	/// Use <see cref="FilteredMarketDepthAdapter"/>.
	/// </summary>
	public bool SupportFilteredMarketDepth
	{
		get => _supportFilteredMarketDepth;
		set
		{
			if (_supportFilteredMarketDepth == value)
				return;

			if (value)
				EnableAdapter(a => new FilteredMarketDepthAdapter(a) { OwnInnerAdapter = true }, typeof(AssociatedSecurityAdapter));
			else
				DisableAdapter<FilteredMarketDepthAdapter>();

			_supportFilteredMarketDepth = value;
		}
	}

	private bool _supportSnapshots = true;

	/// <summary>
	/// Use <see cref="SnapshotHolderMessageAdapter"/>.
	/// </summary>
	public virtual bool SupportSnapshots
	{
		get => _supportSnapshots;
		set
		{
			if (_supportSnapshots == value)
				return;

			if (value)
				EnableAdapter(a => new SnapshotHolderMessageAdapter(a, _entityCache) { OwnInnerAdapter = true }, typeof(BasketSecurityMessageAdapter));
			else
				DisableAdapter<SnapshotHolderMessageAdapter>();

			_supportSnapshots = value;
		}
	}

	private bool _supportAssociatedSecurity;

	/// <summary>
	/// Use <see cref="AssociatedSecurityAdapter"/>.
	/// </summary>
	public bool SupportAssociatedSecurity
	{
		get => _supportAssociatedSecurity;
		set
		{
			if (_supportAssociatedSecurity == value)
				return;

			if (value)
				EnableAdapter(a => new AssociatedSecurityAdapter(a) { OwnInnerAdapter = true }, typeof(SnapshotHolderMessageAdapter));
			else
				DisableAdapter<AssociatedSecurityAdapter>();

			_supportAssociatedSecurity = value;
		}
	}

	/// <summary>
	/// Use <see cref="Level1DepthBuilderAdapter"/>.
	/// </summary>
	[Obsolete("Use MarketDataMessage.BuildFrom property.")]
	public bool SupportLevel1DepthBuilder { get; set; }

	/// <summary>
	/// Storage buffer.
	/// </summary>
	public IStorageBuffer Buffer { get; }

	private (IMessageAdapter prev, IMessageAdapter adapter, IMessageAdapter next) GetAdapter(Type type)
	{
		var adapter = _inAdapter;

		if (adapter == null)
			return default;

		var prev = adapter?.InnerAdapter;
		var next = (IMessageAdapter)null;

		while (true)
		{
			if (adapter.GetType() == type)
				return (prev, adapter, next);

			next = adapter;
			adapter = prev as IMessageAdapterWrapper;

			if (adapter == null)
				return default;

			prev = adapter?.InnerAdapter;
		}
	}

	private (IMessageAdapter prev, IMessageAdapter adapter, IMessageAdapter next) GetAdapter<T>()
		where T : IMessageAdapterWrapper
	{
		return GetAdapter(typeof(T));
	}

	private void EnableAdapter(Func<IMessageAdapter, IMessageAdapterWrapper> create, Type type)
	{
		if (_inAdapter == null)
			return;

		var tuple = type != null ? GetAdapter(type) : default;
		var adapter = tuple.adapter;

		if (adapter != null)
		{
			//if (after)
			//{
			if (tuple.next is IMessageAdapterWrapper nextWrapper)
				nextWrapper.InnerAdapter = create(adapter);
			else
				AddAdapter(create);
			//}
			//else
			//{
			//	var prevWrapper = tuple.prev;
			//	var nextWrapper = adapter as IMessageAdapterWrapper;

			//	if (prevWrapper == null)
			//		throw new InvalidOperationException("Adapter wrapper cannot be added to the beginning of the chain.");

			//	if (nextWrapper == null)
			//		throw new InvalidOperationException(LocalizedStrings.TypeNotImplemented.Put(adapter.GetType(), nameof(IMessageAdapterWrapper)));

			//	nextWrapper.InnerAdapter = create(prevWrapper);
			//}
		}
		else
			AddAdapter(create);
	}

	private void AddAdapter(Func<IMessageAdapter, IMessageAdapterWrapper> create)
	{
		_inAdapter.NewOutMessageAsync -= AdapterOnNewOutMessage;

		_inAdapter = create(_inAdapter);
		_inAdapter.NewOutMessageAsync += AdapterOnNewOutMessage;
	}

	private void DisableAdapter<T>()
		where T : IMessageAdapterWrapper
	{
		var tuple = GetAdapter<T>();

		if (tuple == default)
			return;

		var adapterWrapper = (MessageAdapterWrapper)tuple.adapter;
		var nextWrapper = (MessageAdapterWrapper)tuple.next;

		if (nextWrapper == null)
		{
			adapterWrapper.NewOutMessageAsync -= AdapterOnNewOutMessage;

			_inAdapter = (IMessageAdapterWrapper)adapterWrapper.InnerAdapter;
			_inAdapter.NewOutMessageAsync += AdapterOnNewOutMessage;
		}
		else
			nextWrapper.InnerAdapter = adapterWrapper.InnerAdapter;

		adapterWrapper.OwnInnerAdapter = false;
		adapterWrapper.Dispose();
	}

	private void InnerAdaptersOnAdded(IMessageAdapter adapter)
	{
		if (adapter.IsTransactional())
			TransactionAdapter = adapter;

		if (adapter.IsMarketData())
			MarketDataAdapter = adapter;
	}

	private void InnerAdaptersOnRemoved(IMessageAdapter adapter)
	{
		if (TransactionAdapter == adapter)
			TransactionAdapter = null;

		if (MarketDataAdapter == adapter)
			MarketDataAdapter = null;
	}

	private void InnerAdaptersOnCleared()
	{
		TransactionAdapter = null;
		MarketDataAdapter = null;
	}

	/// <inheritdoc />
	public IMessageAdapter TransactionAdapter { get; private set; }

	/// <inheritdoc />
	public IMessageAdapter MarketDataAdapter { get; private set; }

	/// <summary>
	/// Storage adapter.
	/// </summary>
	public StorageMetaInfoMessageAdapter StorageAdapter { get; private set; }

	private ValueTask SendMessage(IMessageChannel channel, Message message, CancellationToken cancellationToken)
	{
		if (channel is null)
			return default;

		message.TryInitLocalTime(this);

		if (!channel.IsOpened())
			channel.Open();

		return channel.SendInMessageAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	public ValueTask SendInMessageAsync(Message message, CancellationToken cancellationToken)
		=> SendMessage(InMessageChannel, message, cancellationToken);

	/// <inheritdoc />
	public ValueTask SendOutMessageAsync(Message message, CancellationToken cancellationToken)
		=> SendMessage(OutMessageChannel, message, cancellationToken);

	/// <inheritdoc />
	[Obsolete("Use SendInMessageAsync instead.")]
	public void SendInMessage(Message message)
		=> _ = SendInMessageAsync(message, default);

	/// <inheritdoc />
	[Obsolete("Use SendOutMessageAsync instead.")]
	public void SendOutMessage(Message message)
		=> _ = SendOutMessageAsync(message, default);

	/// <summary>
	/// Send error message.
	/// </summary>
	/// <param name="error">Error details.</param>
	public void SendOutError(Exception error)
		=> AsyncHelper.Run(() => SendOutErrorAsync(error, default));

	/// <summary>
	/// Send error message.
	/// </summary>
	/// <param name="error">Error details.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	public ValueTask SendOutErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutMessageAsync(error.ToErrorMessage(), cancellationToken);

	/// <summary>
	/// Process message.
	/// </summary>
	/// <param name="message">Message.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	protected virtual async ValueTask OnProcessMessage(Message message, CancellationToken cancellationToken)
	{
		if (message.Type is not MessageTypes.Time and not MessageTypes.QuoteChange)
			LogVerbose("BP:{0}", message);

		ProcessTimeInterval(message);

		await RaiseNewMessage(message, cancellationToken);

		try
		{
			switch (message.Type)
			{
				case MessageTypes.Connect:
					await _messageProcessor.ProcessConnectMessage((ConnectMessage)message, cancellationToken);
					break;

				case MessageTypes.Disconnect:
					_messageProcessor.ProcessDisconnectMessage((DisconnectMessage)message);
					break;

				case MessageTypes.ConnectionLost:
					_messageProcessor.ProcessConnectionLostMessage(message);
					break;

				case MessageTypes.ConnectionRestored:
					_messageProcessor.ProcessConnectionRestoredMessage(message);
					break;

				case MessageTypes.QuoteChange:
					await _messageProcessor.ProcessQuotesMessage((QuoteChangeMessage)message, cancellationToken);
					break;

				case MessageTypes.Board:
					_messageProcessor.ProcessBoardMessage((BoardMessage)message);
					break;

				case MessageTypes.BoardState:
					_messageProcessor.ProcessBoardStateMessage((BoardStateMessage)message);
					break;

				case MessageTypes.Security:
					await _messageProcessor.ProcessSecurityMessage((SecurityMessage)message, cancellationToken);
					break;

				case MessageTypes.DataTypeInfo:
					_messageProcessor.ProcessDataTypeInfoMessage((DataTypeInfoMessage)message);
					break;

				case MessageTypes.Level1Change:
					await _messageProcessor.ProcessLevel1ChangeMessage((Level1ChangeMessage)message, cancellationToken);
					break;

				case MessageTypes.News:
					await _messageProcessor.ProcessNewsMessage((NewsMessage)message, cancellationToken);
					break;

				case MessageTypes.Execution:
					await _messageProcessor.ProcessExecutionMessage((ExecutionMessage)message, cancellationToken);
					break;

				case MessageTypes.Portfolio:
					_messageProcessor.ProcessPortfolioMessage((PortfolioMessage)message);
					break;

				case MessageTypes.PositionChange:
					await _messageProcessor.ProcessPositionChangeMessage((PositionChangeMessage)message, cancellationToken);
					break;

				//case MessageTypes.Time:
				//	break;

				case MessageTypes.SubscriptionResponse:
					_messageProcessor.ProcessSubscriptionResponseMessage((SubscriptionResponseMessage)message);
					break;

				case MessageTypes.SubscriptionFinished:
					await _messageProcessor.ProcessSubscriptionFinishedMessage((SubscriptionFinishedMessage)message, cancellationToken);
					break;

				case MessageTypes.SubscriptionOnline:
					_messageProcessor.ProcessSubscriptionOnlineMessage((SubscriptionOnlineMessage)message);
					break;

				case MessageTypes.Error:
					_messageProcessor.ProcessErrorMessage((ErrorMessage)message);
					break;

				case ExtendedMessageTypes.RemoveSecurity:
					await _messageProcessor.ProcessSecurityRemoveMessage((SecurityRemoveMessage)message, cancellationToken);
					break;

				case MessageTypes.ChangePassword:
					_messageProcessor.ProcessChangePasswordMessage((ChangePasswordMessage)message);
					break;

				default:
				{
					if (message is CandleMessage candleMsg)
						_messageProcessor.ProcessCandleMessage(candleMsg);
					else if (message is ISubscriptionIdMessage subscrMsg)
						_messageProcessor.ProcessSubscriptionMessage(subscrMsg);

					// если адаптеры передают специфичные сообщения
					// throw new ArgumentOutOfRangeException(LocalizedStrings.UnknownType.Put(message.Type));
					break;
				}
			}
		}
		catch (Exception ex)
		{
			RaiseError(new InvalidOperationException(LocalizedStrings.MessageCauseError.Put(message), ex));
		}
	}


	internal void ProcessSubscriptionResult(Subscription subscription, object[] items)
	{
		T[] typed<T>() => items.Cast<T>().ToArray();

		if (subscription.SubscriptionMessage is SecurityLookupMessage secLookup)
		{
			RaiseLookupSecuritiesResult(secLookup, null, typed<Security>());
		}
		else if (subscription.SubscriptionMessage is PortfolioLookupMessage pfLookup)
		{
			RaiseLookupPortfoliosResult(pfLookup, null, typed<Portfolio>());
		}
	}

	/// <inheritdoc />
	public Portfolio LookupByPortfolioName(string name) => GetPortfolio(name, null, out _);

	/// <summary>
	/// To get the portfolio by the code name.
	/// </summary>
	/// <param name="name">Portfolio code name.</param>
	/// <returns>The got portfolio. If there is no portfolio by given criteria, <see langword="null" /> is returned.</returns>
	public Portfolio GetPortfolio(string name) => LookupByPortfolioName(name);

	internal Portfolio GetPortfolio(string name, Func<Portfolio, bool> changePortfolio, out bool isNew)
	{
		if (name.IsEmpty())
			throw new ArgumentNullException(nameof(name));

		var portfolio = PositionStorage.GetOrCreatePortfolio(name,
			key => new Portfolio { Name = key } ?? throw new InvalidOperationException(LocalizedStrings.PortfolioNotCreated.Put(name)),
			out isNew);

		var isChanged = false;
		if (changePortfolio != null)
			isChanged = changePortfolio(portfolio);

		if (_existingPortfolios.TryAdd(portfolio))
		{
			LogInfo(LocalizedStrings.NewPortfolioCreated, portfolio.Name);
			RaiseNewPortfolio(portfolio);
		}
		else if (isChanged)
			RaisePortfolioChanged(portfolio);

		return portfolio;
	}
}