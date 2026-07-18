namespace StockSharp.Algo;

/// <summary>
/// Abstraction over the connector's inbound-message handlers.
/// </summary>
/// <remarks>
/// The <see cref="Connector"/> façade retains its <c>OnProcessMessage</c> method and its
/// <c>switch (message.Type)</c> statement, delegating each case to the matching member of this
/// interface. The members are declared below in the same order in which that dispatch invokes them.
/// Implementations must reproduce the original handlers' side effects and their ordering exactly,
/// because downstream strategies depend on the resulting event sequence.
/// </remarks>
public interface IConnectorMessageProcessor
{
	/// <summary>
	/// Processes a connection confirmation received from an adapter, raising the connected or connection-error events.
	/// </summary>
	/// <param name="message">The <see cref="ConnectMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessConnectMessage(ConnectMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes a disconnection confirmation received from an adapter, raising the disconnected or connection-error events.
	/// </summary>
	/// <param name="message">The <see cref="DisconnectMessage"/> to process.</param>
	void ProcessDisconnectMessage(DisconnectMessage message);

	/// <summary>
	/// Processes a connection-lost notification, raising the connection-lost event for the originating adapter.
	/// </summary>
	/// <param name="message">The connection-lost message. The base <see cref="Message"/> type is used because only the originating adapter is required.</param>
	void ProcessConnectionLostMessage(Message message);

	/// <summary>
	/// Processes a connection-restored notification, raising the connection-restored event for the originating adapter.
	/// </summary>
	/// <param name="message">The connection-restored message. The base <see cref="Message"/> type is used because only the originating adapter is required.</param>
	void ProcessConnectionRestoredMessage(Message message);

	/// <summary>
	/// Processes an order book update, raising the order book received event.
	/// </summary>
	/// <param name="message">The <see cref="QuoteChangeMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessQuotesMessage(QuoteChangeMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes an exchange board definition, creating or updating the board and raising the board received event.
	/// </summary>
	/// <param name="message">The <see cref="BoardMessage"/> to process.</param>
	void ProcessBoardMessage(BoardMessage message);

	/// <summary>
	/// Processes an exchange board state change, raising the board received event.
	/// </summary>
	/// <param name="message">The <see cref="BoardStateMessage"/> to process.</param>
	void ProcessBoardStateMessage(BoardStateMessage message);

	/// <summary>
	/// Processes a security definition, creating or updating the security and raising the security received event.
	/// </summary>
	/// <param name="message">The <see cref="SecurityMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessSecurityMessage(SecurityMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes a data type information lookup response, raising the data type received event.
	/// </summary>
	/// <param name="message">The <see cref="DataTypeInfoMessage"/> to process.</param>
	void ProcessDataTypeInfoMessage(DataTypeInfoMessage message);

	/// <summary>
	/// Processes a level1 change, updating cached level1 data and raising the level1 received event.
	/// </summary>
	/// <param name="message">The <see cref="Level1ChangeMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessLevel1ChangeMessage(Level1ChangeMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes a news item, raising the news received event.
	/// </summary>
	/// <param name="message">The <see cref="NewsMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessNewsMessage(NewsMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes an execution, dispatching it to transaction, tick, or order-log handling according to its data type.
	/// </summary>
	/// <param name="message">The <see cref="ExecutionMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessExecutionMessage(ExecutionMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes a portfolio definition, creating or updating the portfolio and raising the portfolio received event.
	/// </summary>
	/// <param name="message">The <see cref="PortfolioMessage"/> to process.</param>
	void ProcessPortfolioMessage(PortfolioMessage message);

	/// <summary>
	/// Processes a position change, updating the portfolio or position and raising the corresponding changed and received events.
	/// </summary>
	/// <param name="message">The <see cref="PositionChangeMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessPositionChangeMessage(PositionChangeMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes a subscription response, raising the appropriate market-data or subscription success and failure events.
	/// </summary>
	/// <param name="replyMsg">The <see cref="SubscriptionResponseMessage"/> to process.</param>
	void ProcessSubscriptionResponseMessage(SubscriptionResponseMessage replyMsg);

	/// <summary>
	/// Processes a subscription finished notification, delivering any embedded security or board items and raising the market-data subscription finished event.
	/// </summary>
	/// <param name="message">The <see cref="SubscriptionFinishedMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessSubscriptionFinishedMessage(SubscriptionFinishedMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes a subscription online notification, raising the market-data subscription online event.
	/// </summary>
	/// <param name="message">The <see cref="SubscriptionOnlineMessage"/> to process.</param>
	void ProcessSubscriptionOnlineMessage(SubscriptionOnlineMessage message);

	/// <summary>
	/// Processes a general error, raising the error event.
	/// </summary>
	/// <param name="message">The <see cref="ErrorMessage"/> to process.</param>
	void ProcessErrorMessage(ErrorMessage message);

	/// <summary>
	/// Processes a security removal notification, removing the matching securities and raising the security removed event.
	/// </summary>
	/// <param name="message">The <see cref="SecurityRemoveMessage"/> to process.</param>
	/// <param name="cancellationToken"><see cref="CancellationToken"/></param>
	/// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
	ValueTask ProcessSecurityRemoveMessage(SecurityRemoveMessage message, CancellationToken cancellationToken);

	/// <summary>
	/// Processes a change-password result, raising the change-password event.
	/// </summary>
	/// <param name="message">The <see cref="ChangePasswordMessage"/> to process.</param>
	void ProcessChangePasswordMessage(ChangePasswordMessage message);

	/// <summary>
	/// Processes a candle update, delivering it to matching subscriptions and raising the candle received event.
	/// </summary>
	/// <param name="message">The <see cref="CandleMessage"/> to process.</param>
	void ProcessCandleMessage(CandleMessage message);

	/// <summary>
	/// Processes any remaining subscription-bearing message, raising the subscription received event.
	/// </summary>
	/// <param name="subscrMsg">The <see cref="ISubscriptionIdMessage"/> to process.</param>
	void ProcessSubscriptionMessage(ISubscriptionIdMessage subscrMsg);
}
