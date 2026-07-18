namespace StockSharp.Algo;

/// <summary>
/// Default implementation of <see cref="IConnectorMessageProcessor"/> hosting the
/// inbound-message handler bodies extracted from the <see cref="Connector"/> partial
/// class. Each handler is invoked by the connector facade's <c>OnProcessMessage</c>
/// dispatch switch, preserving the original side effects and their ordering.
/// </summary>
public class ConnectorMessageProcessor : IConnectorMessageProcessor
{
	private readonly Connector _connector;
	private readonly EntityCache _entityCache;
	private readonly IConnectorSubscriptionManager _subscriptionManager;

	/// <summary>
	/// Initializes a new instance of the <see cref="ConnectorMessageProcessor"/> class.
	/// </summary>
	/// <param name="connector">The owning <see cref="Connector"/> facade whose state and event
	/// forwarders the handlers operate against. Cannot be <see langword="null"/>.</param>
	/// <param name="entityCache">The entity-cache seam the handlers read from and update. Injected as
	/// a focused dependency so the facade keeps it private rather than exposing it as an internal
	/// field. Cannot be <see langword="null"/>.</param>
	/// <param name="subscriptionManager">The subscription-manager seam the handlers query and drive.
	/// Injected as a focused, interface-typed dependency. Cannot be <see langword="null"/>.</param>
	public ConnectorMessageProcessor(Connector connector, EntityCache entityCache, IConnectorSubscriptionManager subscriptionManager)
	{
		_connector = connector ?? throw new ArgumentNullException(nameof(connector));
		_entityCache = entityCache ?? throw new ArgumentNullException(nameof(entityCache));
		_subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
	}

	/// <inheritdoc />
	public async ValueTask ProcessConnectMessage(ConnectMessage message, CancellationToken cancellationToken)
	{
		var adapter = message.Adapter;
		var error = message.Error;

		if (error == null)
		{
			if (adapter == _connector.Adapter)
			{
				await _connector.ApplySubscriptionManagerActionsAsync(_subscriptionManager.HandleConnected(subscription => _connector.Adapter.IsMessageSupported(subscription.SubscriptionMessage.Type)), cancellationToken);

				// raise event after re subscriptions cause handler on Connected event can send some subscriptions
				_connector.RaiseConnected();
			}
			else
				_connector.RaiseConnectedEx(adapter);
		}
		else
		{
			if (adapter == _connector.Adapter)
				_connector.RaiseConnectionError(error);
			else
				_connector.RaiseConnectionErrorEx(adapter, error);
		}
	}

	/// <inheritdoc />
	public void ProcessDisconnectMessage(DisconnectMessage message)
	{
		var adapter = message.Adapter;
		var error = message.Error;

		if (error == null)
		{
			if (adapter == _connector.Adapter)
				_connector.RaiseDisconnected();
			else
				_connector.RaiseDisconnectedEx(adapter);
		}
		else
		{
			if (adapter == _connector.Adapter)
				_connector.RaiseConnectionError(error);
			else
				_connector.RaiseConnectionErrorEx(adapter, error);
		}
	}

	/// <inheritdoc />
	public void ProcessConnectionLostMessage(Message message)
	{
		_connector.RaiseConnectionLost(message.Adapter);
	}

	/// <inheritdoc />
	public void ProcessConnectionRestoredMessage(Message message)
	{
		_connector.RaiseConnectionRestored(message.Adapter);
	}

	/// <inheritdoc />
	public async ValueTask ProcessQuotesMessage(QuoteChangeMessage message, CancellationToken cancellationToken)
	{
		if (_connector.RaiseReceived(message, message, _connector.OrderBookReceivedEvent) != true)
			return;

		if (message.IsFiltered || message.State != null)
			return;

		_entityCache.UpdateOrderBookSnapshot(message);

		var bestBid = message.GetBestBid();
		var bestAsk = message.GetBestAsk();
		var fromLevel1 = message.BuildFrom == DataType.Level1;
		var time = message.ServerTime;

		Security security = null;

		if (_connector.ValuesChangedEvent is not null && !fromLevel1 && !_connector.Adapter.Level1Extend && (bestBid != null || bestAsk != null))
		{
			security ??= await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			var info = _entityCache.GetSecurityValues(security, time);

			info.ClearBestQuotes(time);

			var changes = new List<KeyValuePair<Level1Fields, object>>(4);

			if (bestBid != null)
			{
				var q = bestBid.Value;

				info.SetValue(time, Level1Fields.BestBidPrice, q.Price);
				changes.Add(new (Level1Fields.BestBidPrice, q.Price));

				if (q.Volume != 0)
				{
					info.SetValue(time, Level1Fields.BestBidVolume, q.Volume);
					changes.Add(new (Level1Fields.BestBidVolume, q.Volume));
				}
			}

			if (bestAsk != null)
			{
				var q = bestAsk.Value;

				info.SetValue(time, Level1Fields.BestAskPrice, q.Price);
				changes.Add(new (Level1Fields.BestAskPrice, q.Price));

				if (q.Volume != 0)
				{
					info.SetValue(time, Level1Fields.BestAskVolume, q.Volume);
					changes.Add(new (Level1Fields.BestAskVolume, q.Volume));
				}
			}

			_connector.RaiseValuesChanged(security, changes, message.ServerTime, message.LocalTime);
		}

#pragma warning disable CS0618 // Type or member is obsolete
		if (_connector.UpdateSecurityLastQuotes)
		{
			security ??= await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			var updated = false;

			if (!fromLevel1 || bestBid != null)
			{
				updated = true;
				security.BestBid = bestBid;
			}

			if (!fromLevel1 || bestAsk != null)
			{
				updated = true;
				security.BestAsk = bestAsk;
			}

			if (updated)
			{
				security.LocalTime = message.LocalTime;
				security.LastChangeTime = message.ServerTime;

				// стаканы по ALL обновляют BestXXX по конкретным инструментам
				if (security.Board?.Code == SecurityId.AssociatedBoardCode)
				{
					var changedSecurities = new Dictionary<Security, RefPair<bool, bool>>();

					foreach (var bid in message.Bids)
					{
						if (bid.BoardCode.IsEmpty())
							continue;

						var innerSecurity = await _connector.GetSecurityAsync(new SecurityId
						{
							SecurityCode = security.Code,
							BoardCode = bid.BoardCode
						}, cancellationToken);

						var info = changedSecurities.SafeAdd(innerSecurity);

						if (info.First)
							continue;

						info.First = true;

						innerSecurity.BestBid = bid;
						innerSecurity.LocalTime = message.LocalTime;
						innerSecurity.LastChangeTime = message.ServerTime;
					}

					foreach (var ask in message.Asks)
					{
						if (ask.BoardCode.IsEmpty())
							continue;

						var innerSecurity = await _connector.GetSecurityAsync(new SecurityId
						{
							SecurityCode = security.Code,
							BoardCode = ask.BoardCode
						}, cancellationToken);

						var info = changedSecurities.SafeAdd(innerSecurity);

						if (info.Second)
							continue;

						info.Second = true;

						innerSecurity.BestAsk = ask;
						innerSecurity.LocalTime = message.LocalTime;
						innerSecurity.LastChangeTime = message.ServerTime;
					}
				}
			}
		}
#pragma warning restore CS0618 // Type or member is obsolete
	}

	/// <inheritdoc />
	public void ProcessBoardMessage(BoardMessage message)
	{
		var board = _connector.ExchangeInfoProvider.GetOrCreateBoard(message.Code, out var isNew, code =>
		{
			var b = new ExchangeBoard
			{
				Code = code,
				Exchange = message.ToExchange(),
			};
			return b.ApplyChanges(message);
		});

		var subscriptions = _subscriptionManager.ProcessLookupResponse(message, board);
		_connector.RaiseReceived(board, subscriptions, _connector.BoardReceivedEvent);
	}

	/// <inheritdoc />
	public void ProcessBoardStateMessage(BoardStateMessage message)
	{
		ExchangeBoard board;

		if (message.BoardCode.IsEmpty())
			board = null;
		else
			board = _connector.ExchangeInfoProvider.GetOrCreateBoard(message.BoardCode);

		_connector.RaiseReceived(board, message, _connector.BoardReceivedEvent);
	}

	/// <inheritdoc />
	public async ValueTask ProcessSecurityMessage(SecurityMessage message, CancellationToken cancellationToken)
	{
		var security = await _connector.GetSecurityAsync(message.SecurityId, s =>
		{
			if (!_connector.UpdateSecurityByDefinition)
				return false;

			s.ApplyChanges(message, _connector.ExchangeInfoProvider, _connector.OverrideSecurityData);
			return true;
		}, cancellationToken);

		var subscriptions = _subscriptionManager.ProcessLookupResponse(message, security);
		_connector.RaiseReceived(security, subscriptions, _connector.SecurityReceivedEvent);
	}

	/// <inheritdoc />
	public void ProcessDataTypeInfoMessage(DataTypeInfoMessage message)
	{
		var dt = message.FileDataType ?? throw new InvalidOperationException(LocalizedStrings.NoDataTypeSelected);

		_subscriptionManager.ProcessLookupResponse(message, dt);
		_connector.RaiseReceived(dt, message, _connector.DataTypeReceivedEvent);
	}

	/// <inheritdoc />
	public async ValueTask ProcessLevel1ChangeMessage(Level1ChangeMessage message, CancellationToken cancellationToken)
	{
		Security security = null;

		if (_connector.RaiseReceived(message, message, _connector.RaiseLevel1Received, out var anyCanOnline) != true)
		{
			if (anyCanOnline != true)
				return;

			security = await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			if (_entityCache.HasLevel1Info(security))
				return;
		}

#pragma warning disable CS0618 // Type or member is obsolete
		if (_connector.UpdateSecurityByLevel1)
		{
			security ??= await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			security.ApplyChanges(message);
		}
#pragma warning restore CS0618 // Type or member is obsolete

		if (_connector.ValuesChangedEvent is not null)
		{
			security ??= await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			var time = message.ServerTime;
			var info = _entityCache.GetSecurityValues(security, time);

			var changes = message.Changes;
			var cloned = false;

			foreach (var change in message.Changes)
			{
				var field = change.Key;

				if (!info.CanLastTrade && field.IsLastTradeField())
				{
					if (!cloned)
					{
						changes = changes.ToDictionary();
						cloned = true;
					}

					changes.Remove(field);

					continue;
				}

				if (!info.CanBestQuotes && (field.IsBestBidField() || field.IsBestAskField()))
				{
					if (!cloned)
					{
						changes = changes.ToDictionary();
						cloned = true;
					}

					changes.Remove(field);

					continue;
				}

				info.SetValue(time, field, change.Value);
			}

			if (changes.Count > 0)
				_connector.RaiseValuesChanged(security, message.Changes, message.ServerTime, message.LocalTime);
		}
	}

	/// <inheritdoc />
	public async ValueTask ProcessNewsMessage(NewsMessage message, CancellationToken cancellationToken)
	{
		var security = message.SecurityId == null ? null : await _connector.GetSecurityAsync(message.SecurityId.Value, cancellationToken);

		var news = _entityCache.ProcessNewsMessage(security, message);

		if (_connector.RaiseReceived(news.news, message, _connector.NewsReceivedEvent) == false)
			return;
	}

	/// <inheritdoc />
	public async ValueTask ProcessExecutionMessage(ExecutionMessage message, CancellationToken cancellationToken)
	{
		if (message.DataType == DataType.Transactions)
			await ProcessTransactionMessage(message, cancellationToken);
		else if (message.DataType == DataType.Ticks)
			await ProcessTradeMessage(message, cancellationToken);
		else if (message.DataType == DataType.OrderLog)
			ProcessOrderLogMessage(message);
		else
			throw new ArgumentOutOfRangeException(nameof(message), message.DataType, LocalizedStrings.UnknownType.Put(message));
	}

	/// <inheritdoc />
	public void ProcessPortfolioMessage(PortfolioMessage message)
	{
		var portfolio = _connector.GetPortfolio(message.PortfolioName, p =>
		{
			message.ToPortfolio(p, _connector.ExchangeInfoProvider);
			return true;
		}, out var isNew);

		//if (message.OriginalTransactionId == 0)
		//	return;

		if (isNew)
			_subscriptionManager.ProcessLookupResponse(message, portfolio);

		_connector.RaiseReceived(portfolio, message, _connector.PortfolioReceivedEvent);
	}

	/// <inheritdoc />
	public async ValueTask ProcessPositionChangeMessage(PositionChangeMessage message, CancellationToken cancellationToken)
	{
		if (!message.StrategyId.IsEmpty())
			return;

		Portfolio portfolio;

		if (message.IsMoney())
		{
			portfolio = _connector.GetPortfolio(message.PortfolioName, pf =>
			{
				if (message.LimitType != null || !_connector.UpdatePortfolioByChange)
					return false;

				pf.ApplyChanges(message, _connector.ExchangeInfoProvider);
				return true;
			}, out _);

			_connector.RaiseReceived(portfolio, message, _connector.PortfolioReceivedEvent);
		}

		var security = await _connector.EnsureGetSecurityAsync(message, cancellationToken);
		portfolio = _connector.LookupByPortfolioName(message.PortfolioName);

		var valueInLots = message.TryGetDecimal(PositionChangeTypes.CurrentValueInLots);
		if (valueInLots != null)
		{
			if (!message.Changes.ContainsKey(PositionChangeTypes.CurrentValue))
			{
				var currValue = (decimal)valueInLots / (security.VolumeStep ?? 1);
				message.Add(PositionChangeTypes.CurrentValue, currValue);
			}

			message.Changes.Remove(PositionChangeTypes.CurrentValueInLots);
		}

		var position = _connector.GetPosition(portfolio, security, message.StrategyId, message.Side, message.ClientCode, message.DepoName, message.LimitType, message.Description);
		position.ApplyChanges(message);

		_connector.RaisePositionChanged(position);
		_connector.RaiseReceived(position, message, _connector.PositionReceivedEvent);
	}

	/// <inheritdoc />
	public void ProcessSubscriptionResponseMessage(SubscriptionResponseMessage replyMsg)
	{
		var error = replyMsg.Error;

		var subscription = _subscriptionManager.ProcessResponse(replyMsg, out var originalMsg, out var unexpectedCancelled, out var items);

		if (originalMsg == null)
		{
			if (error != null)
				_connector.RaiseErrorCore(error);

			return;
		}

		if (originalMsg is MarketDataMessage mdMdg)
		{
			if (originalMsg.IsSubscribe)
			{
				if (replyMsg.IsOk())
					_connector.RaiseMarketDataSubscriptionSucceeded(mdMdg, subscription);
				else
				{
					if (unexpectedCancelled)
						_connector.RaiseMarketDataUnexpectedCancelled(mdMdg, error ?? new NotSupportedException(LocalizedStrings.SubscriptionNotSupported.Put(originalMsg)), subscription);
					else
						_connector.RaiseMarketDataSubscriptionFailed(mdMdg, replyMsg, subscription);
				}
			}
			else
			{
				if (replyMsg.IsOk())
					_connector.RaiseMarketDataUnSubscriptionSucceeded(mdMdg, subscription);
				else
					_connector.RaiseMarketDataUnSubscriptionFailed(mdMdg, replyMsg, subscription);
			}
		}
		else
		{
			if (error == null)
				_connector.RaiseSubscriptionStarted(subscription);
			else
			{
				_connector.RaiseSubscriptionFailedCore(subscription, error, originalMsg.IsSubscribe);

				T[] typed<T>() => items.Cast<T>().ToArray();

				if (originalMsg is SecurityLookupMessage secLookup)
					_connector.RaiseLookupSecuritiesResult(secLookup, error, typed<Security>());
				else if (originalMsg is PortfolioLookupMessage pfLookup)
					_connector.RaiseLookupPortfoliosResult(pfLookup, error, typed<Portfolio>());
			}
		}
	}

	/// <inheritdoc />
	public async ValueTask ProcessSubscriptionFinishedMessage(SubscriptionFinishedMessage message, CancellationToken cancellationToken)
	{
		var subscription = _subscriptionManager.ProcessSubscriptionFinishedMessage(message, out var items);

		if (subscription == null)
			return;

		if (message.Body?.Length > 0)
		{
			if (subscription.DataType == DataType.Securities)
			{
				var secMsgs = new List<SecurityMessage>();

				await foreach (var secMsg in message.Body.ExtractSecuritiesAsync().WithCancellation(cancellationToken))
				{
					await ProcessSecurityMessage(secMsg, cancellationToken);
					secMsgs.Add(secMsg);
				}

				items = [.. items, .. secMsgs];
			}
			else if (subscription.DataType == DataType.Board)
			{
				var boardMsgs = new List<BoardMessage>();

				await foreach (var boardMsg in message.Body.ExtractBoardsAsync().WithCancellation(cancellationToken))
				{
					ProcessBoardMessage(boardMsg);
					boardMsgs.Add(boardMsg);
				}

				items = [.. items, .. boardMsgs];
			}
		}

		_connector.RaiseMarketDataSubscriptionFinished(message, subscription);

		_connector.ProcessSubscriptionResult(subscription, items);
	}

	/// <inheritdoc />
	public void ProcessSubscriptionOnlineMessage(SubscriptionOnlineMessage message)
	{
		var subscription = _subscriptionManager.ProcessSubscriptionOnlineMessage(message, out var items);

		if (subscription == null)
			return;

		_connector.RaiseMarketDataSubscriptionOnline(subscription);

		_connector.ProcessSubscriptionResult(subscription, items);
	}

	/// <inheritdoc />
	public void ProcessErrorMessage(ErrorMessage message)
	{
		_connector.RaiseErrorCore(message.Error);
	}

	/// <inheritdoc />
	public async ValueTask ProcessSecurityRemoveMessage(SecurityRemoveMessage message, CancellationToken cancellationToken)
	{
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		var securityId = message.SecurityId;

		var security = _connector.SecurityStorage.LookupById(securityId);

		if (security != null)
		{
			await _connector.SecurityStorage.DeleteAsync(security, cancellationToken);
			_connector.FireSecuritiesRemoved([security]);
		}
	}

	/// <inheritdoc />
	public void ProcessChangePasswordMessage(ChangePasswordMessage message)
	{
		_connector.RaiseChangePassword(message.OriginalTransactionId, message.Error);
	}

	/// <inheritdoc />
	public void ProcessCandleMessage(CandleMessage message)
	{
		foreach (var (subscription, candle) in _subscriptionManager.UpdateCandles(message))
		{
			_connector.CandleReceivedEvent?.Invoke(subscription, candle);
			_connector.RaiseSubscriptionReceived(subscription, message);
		}
	}

	/// <inheritdoc />
	public void ProcessSubscriptionMessage(ISubscriptionIdMessage subscrMsg)
	{
		_connector.RaiseReceived((Message)subscrMsg, subscrMsg, _connector.RaiseSubscriptionReceived);
	}

	private void ProcessOrderLogMessage(ExecutionMessage message)
	{
		if (_connector.RaiseReceived(message, message, _connector.OrderLogReceivedEvent) == false)
			return;
	}

	private async ValueTask ProcessTradeMessage(ExecutionMessage message, CancellationToken cancellationToken)
	{
		if (_connector.RaiseReceived(message, message, _connector.TickTradeReceivedEvent) != true)
			return;

		Security security = null;

		if (_connector.ValuesChangedEvent is not null)
		{
			security ??= await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			var time = message.ServerTime;
			var info = _entityCache.GetSecurityValues(security, time);

			info.ClearLastTrade(time);

			var price = message.TradePrice ?? 0;

			var changes = new List<KeyValuePair<Level1Fields, object>>(4)
			{
				new (Level1Fields.LastTradeTime, message.ServerTime),
				new (Level1Fields.LastTradePrice, price)
			};

			info.SetValue(time, Level1Fields.LastTradeTime, message.ServerTime);
			info.SetValue(time, Level1Fields.LastTradePrice, price);

			if (message.IsSystem is bool isSystem)
			{
				info.SetValue(time, Level1Fields.IsSystem, isSystem);
				changes.Add(new(Level1Fields.IsSystem, isSystem));
			}

			if (message.TradeId is long tradeId)
			{
				info.SetValue(time, Level1Fields.LastTradeId, tradeId);
				changes.Add(new(Level1Fields.LastTradeId, tradeId));
			}

			if (!message.TradeStringId.IsEmpty())
			{
				info.SetValue(time, Level1Fields.LastTradeStringId, message.TradeStringId);
				changes.Add(new(Level1Fields.LastTradeStringId, message.TradeStringId));
			}

			if (message.TradeVolume is decimal tradeVol)
			{
				info.SetValue(time, Level1Fields.LastTradeVolume, tradeVol);
				changes.Add(new(Level1Fields.LastTradeVolume, tradeVol));
			}

			if (message.OriginSide is Sides side)
			{
				info.SetValue(time, Level1Fields.LastTradeOrigin, side);
				changes.Add(new(Level1Fields.LastTradeOrigin, side));
			}

			if (message.IsUpTick is bool isUpTick)
			{
				info.SetValue(time, Level1Fields.LastTradeUpDown, isUpTick);
				changes.Add(new(Level1Fields.LastTradeUpDown, isUpTick));
			}

			_connector.RaiseValuesChanged(security, changes, message.ServerTime, message.LocalTime);
		}

#pragma warning disable CS0618 // Type or member is obsolete
		if (_connector.UpdateSecurityLastQuotes)
		{
			security ??= await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			security.LastTick = message;
		}
#pragma warning restore CS0618 // Type or member is obsolete
	}

	private void ProcessOrderMessage(Order o, Security security, ExecutionMessage message, long transactionId/*, bool isStatusRequest*/)
	{
		if (message.OrderState != OrderStates.Failed && message.Error == null)
		{
			foreach (var change in _entityCache.ProcessOrderMessage(o, security, message, transactionId, _connector.LookupByPortfolioName))
			{
				if (change == EntityCache.OrderChangeInfo.NotExist)
				{
					_connector.LogWarning(LocalizedStrings.OrderNotFound, message.OrderId.To<string>() ?? message.OrderStringId);
					continue;
				}

				var order = change.Order;

				_entityCache.TrySetAdapter(order, message.Adapter);

				if (change.IsNew)
				{
					_connector.AddOrderInfoLog(order, "New order");

					_connector.RaiseNewOrder(order);
				}
				else if (change.IsChanged)
				{
					_connector.AddOrderInfoLog(order, "Order changed");

					_connector.RaiseOrderChanged(order);

					if (change.IsEdit)
						_connector.RaiseOrderEdited(transactionId, order);
				}

				_connector.RaiseReceived(order, message, _connector.OrderReceivedEvent);
			}
		}
		else
		{
			if (message.OriginalTransactionId == 0)
			{
				_connector.LogError("Unknown error response for order {0}: {1}.", o, message.Error);
				return;
			}

			foreach (var (fail, operation) in _entityCache.ProcessOrderFailMessage(o, security, message))
			{
				var order = fail.Order;

				_entityCache.TrySetAdapter(order, message.Adapter);

				//TryProcessFilteredMarketDepth(fail.Order.Security, message);

				//var isRegisterFail = (fail.Order.Id == null && fail.Order.StringId.IsEmpty()) || fail.Order.Status == OrderStatus.RejectedBySystem;

				_entityCache.AddFail(operation, fail);

				switch (operation)
				{
					case OrderOperations.Register:
					{
						_connector.RaiseOrderRegisterFailed(message.OriginalTransactionId, fail);
						_connector.RaiseReceived(fail, message, _connector.OrderRegisterFailReceivedEvent);
						break;
					}
					case OrderOperations.Cancel:
					{
						_connector.RaiseOrderCancelFailed(message.OriginalTransactionId, fail);
						_connector.RaiseReceived(fail, message, _connector.OrderCancelFailReceivedEvent);
						break;
					}
					case OrderOperations.Edit:
					{
						_connector.RaiseOrderEditFailed(message.OriginalTransactionId, fail);
						_connector.RaiseReceived(fail, message, _connector.OrderEditFailReceivedEvent);
						break;
					}
					default:
						throw new ArgumentOutOfRangeException(operation.ToString());
				}
			}
		}
	}

	private void ProcessOwnTradeMessage(Order order, Security security, ExecutionMessage message, long transactionId)
	{
		var (trade, isNew) = _entityCache.ProcessOwnTradeMessage(order, security, message, transactionId);

		if (trade == null)
			return;

		if (isNew)
			_connector.RaiseNewMyTrade(trade);

		//_connector.LogWarning("Duplicate own trade message: {0}", message);
		_connector.RaiseReceived(trade, message, _connector.OwnTradeReceivedEvent);
	}

	private async ValueTask ProcessTransactionMessage(ExecutionMessage message, CancellationToken cancellationToken)
	{
		var originId = message.OriginalTransactionId;

		if (_entityCache.IsMassCancelation(originId))
		{
			if (message.IsOk())
				_connector.RaiseMassOrderCanceled(originId, message.ServerTime);
			else
				_connector.RaiseMassOrderCancelFailed(originId, message.Error, message.ServerTime);

			return;
		}

		var isStatusRequest = _entityCache.IsOrderStatusRequest(originId);

		if (!message.IsOk() && isStatusRequest)
		{
			// TransId != 0 means contains failed order info (not just status response)
			if (message.TransactionId == 0)
				return;
		}

		Order order = null;

		var transactionId = message.TransactionId;

		if (transactionId == 0)
		{
			transactionId = isStatusRequest || _entityCache.IsMassCancelation(originId) ? 0 : originId;

			if (transactionId == 0)
				order = _entityCache.TryGetOrder(message.OrderId, message.OrderStringId);
		}

		if (transactionId != 0)
		{
			if (message.HasTradeInfo())
				order = _entityCache.TryGetOrder(transactionId, OrderOperations.Register);
			else
				order = _entityCache.TryGetOrder(transactionId, OrderOperations.Edit) ?? _entityCache.TryGetOrder(transactionId, OrderOperations.Cancel) ?? _entityCache.TryGetOrder(transactionId, OrderOperations.Register);
		}

		Security security;

		if (order == null)
		{
			if (message.SecurityId == default)
			{
				_connector.LogWarning(LocalizedStrings.EmptySecId);
				_connector.LogWarning(message.ToString());
				return;
			}

			security = await _connector.EnsureGetSecurityAsync(message, cancellationToken);

			if (transactionId == 0 && isStatusRequest)
				transactionId = _connector.TransactionIdGenerator.GetNextId();
		}
		else
			security = order.Security;

		_connector.LogDebug("Order '{0}': {1}", order?.TransactionId, message);

		var processed = false;

		if (message.HasOrderInfo())
		{
			processed = true;
			ProcessOrderMessage(order, security, message, transactionId);
		}

		if (message.HasTradeInfo())
		{
			processed = true;
			ProcessOwnTradeMessage(order, security, message, transactionId);
		}

		if (!processed)
			throw new ArgumentOutOfRangeException(nameof(message), message.DataType, LocalizedStrings.UnknownType.Put(message));
	}
}
