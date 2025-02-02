﻿using Light.Transmit;
using Light.Transmit.Internals;
using Microsoft.Extensions.Logging;
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Light.Message
{

    /// <summary>
    /// 消息处理器抽象结构<br/>
    /// 消息处理方法签名如下<br/>
    /// public async ValueTask Process(XXXXXMessage message)
    /// </summary>
    public interface IMessageProcessor
    {

    }


    public delegate ValueTask AsyncMessageHandlerDelegate<TMesssage>(TMesssage message) where TMesssage : AbstractNetMessage;


    /// <summary>
    /// 消息路由器
    /// </summary>
    public sealed class AsyncMessageRouter
    {

        private const Int32 BaseIndex = 32767;
        private readonly ILogger<AsyncMessageRouter> logger = LoggerProvider.CreateLogger<AsyncMessageRouter>();
        private readonly AsyncMessageHandlerDelegate<AbstractNetMessage>[] _messageHandlers = new AsyncMessageHandlerDelegate<AbstractNetMessage>[65535];
        private readonly IMessageProcessor _messageProcessor;
        private readonly ChannelReader<AbstractNetMessage> channelReader;
        private CancelCompletionSignal cancelCompletionSignal = new CancelCompletionSignal(true);
        private readonly Boolean ExitWaitProcessComplete;

        /// <summary>
        /// 创建消息路由器并扫描processor内所有处理程序
        /// </summary>
        /// <param name="processor"></param>
        public AsyncMessageRouter(IMessageProcessor processor, ChannelReader<AbstractNetMessage> sourceChannel, Boolean exitWaitProcessComplete = false)
        {
            this.ExitWaitProcessComplete = exitWaitProcessComplete;
            this.channelReader = sourceChannel;
            this._messageProcessor = processor;
            RegisterAllHandlers(processor);
        }

        public Int32 Count
        {
            get
            {
                return channelReader.Count;
            }
        }

        /// <summary>
        /// 开始自动分发Channel中消息
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            await cancelCompletionSignal.CancelAsync();
            cancelCompletionSignal.Reset();
            ThreadPool.QueueUserWorkItem(ProcessMessage, null);
        }


        /// <summary>
        /// 停止自动消息分发任务
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask StopAsync(CancellationToken cancellationToken)
        {
            await cancelCompletionSignal.CancelAsync();
        }

        private async void ProcessMessage(object state)
        {
            var cancelToken = cancelCompletionSignal.Token;
            AbstractNetMessage message = default;
            while (true)
            {
                try
                {
                    if (cancelToken.IsCancellationRequested)
                    {
                        if (!ExitWaitProcessComplete || !channelReader.TryRead(out message))
                        {
                            break;
                        }
                    }
                    else
                    {
                        message = await channelReader.ReadAsync(cancelToken);
                    }
                    await this.RouteAsync(message);
                }
                catch (Exception) { }
                finally
                {
                    message?.Return();
                }
            }
            cancelCompletionSignal.Complete();
        }

        /// <summary>
        /// 手动分发Channel中所有消息
        /// </summary>
        /// <returns></returns>
        public async Task ProcessAll()
        {
            AbstractNetMessage message;
            while (channelReader.TryRead(out message))
            {
                await this.RouteAsync(message);
            }
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="action"></param>
        public void RegisterHandler<T>(AsyncMessageHandlerDelegate<T> action) where T : AbstractNetMessage, new()
        {
            RegisterHandler(MFactory<T>.Kind, action.Method, (message) => action((T)message));
        }


        /// <summary>
        /// 分发消息，路由至处理器处理方法
        /// </summary>
        /// <param name="session"></param>
        /// <param name="message"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<Boolean> RouteAsync(AbstractNetMessage message)
        {
            try
            {
                var handler = _messageHandlers[BaseIndex + message.Kind];
                if (handler == null) return false;
                await handler(message);
            }
            finally
            {
            }
            return true;
        }


        private void RegisterHandler(Int16 kind, MethodInfo originMethod, AsyncMessageHandlerDelegate<AbstractNetMessage> messageHandler)
        {
            if (_messageHandlers[BaseIndex + kind] != null)
            {
                logger.LogWarning("a message handler kind:[{0}] is overwritten..", kind);
            }
            logger.LogDebug("Register Message Handler [{0}] {1}.{2}", kind, originMethod.DeclaringType.Name, originMethod.Name);
            _messageHandlers[BaseIndex + kind] = messageHandler;
        }

        /// <summary>
        /// 自动注册类中的所有处理方法
        /// </summary>
        private void RegisterAllHandlers(IMessageProcessor processor)
        {
            // 获取当前类的所有方法
            var methods = processor.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var method in methods)
            {
                // 方法必须符合以下条件：
                // - 参数列表包含 IConnectionSession 和消息类型（AbstractNetMessage）
                // - 返回值是 void
                var parameters = method.GetParameters();
                if (method.ReturnType == typeof(ValueTask) && parameters.Length == 1 && parameters[0].ParameterType.IsSubclassOf(typeof(AbstractNetMessage)))
                {
                    // 获取消息类型（第二个参数类型）
                    var messageType = parameters[0].ParameterType;
                    // 获取消息类型的 Kind
                    var kindField = typeof(MFactory<>).MakeGenericType(messageType).GetField("Kind");
                    var kind = (Int16)kindField.GetValue(null);
                    // 动态创建 MessageHandlerDelegate<T> 委托
                    var handlerDelegate = CreateHandlerDelegate(messageType, method);
                    // 注册处理器
                    RegisterHandler(kind, method, handlerDelegate);
                }
            }
        }

        private AsyncMessageHandlerDelegate<AbstractNetMessage> CreateHandlerDelegate(Type messageType, MethodInfo method)
        {
            // 定义参数
            var messageParam = Expression.Parameter(typeof(AbstractNetMessage), "message");
            // 将 AbstractNetMessage 转换为具体的消息类型
            var convertedMessageParam = Expression.Convert(messageParam, messageType);
            // 构建方法调用表达式
            var call = Expression.Call(Expression.Constant(this._messageProcessor), method, convertedMessageParam);
            // 如果方法返回值是 ValueTask，直接返回方法调用
            if (method.ReturnType == typeof(ValueTask))
            {
                // 创建 Lambda 表达式
                var lambda = Expression.Lambda<AsyncMessageHandlerDelegate<AbstractNetMessage>>(call, messageParam);
                return lambda.Compile();
            }
            else
            {
                // 如果方法返回值不是 ValueTask，抛出异常或转换为 ValueTask
                throw new InvalidOperationException("Method must return ValueTask.");
            }
        }

    }


}
