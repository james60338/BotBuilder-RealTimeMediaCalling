﻿// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Bot Framework: http://botframework.com
// 
// Bot Builder SDK GitHub:
// https://github.com/Microsoft/BotBuilder
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Bot.Builder.Calling.ObjectModel.Misc;

namespace Microsoft.Bot.Builder.RealTimeMediaCalling
{
    /// <summary>
    /// The top level class that is used to register the bot for enabling real-time media communication
    /// </summary>
    public static class RealTimeMediaCalling
    {
        private static readonly IContainer Container;

        static RealTimeMediaCalling()
        {
            var builder = new ContainerBuilder();
            builder.RegisterModule(new RealTimeMediaCallingModule_MakeBot());
            Container = builder.Build();
        }

        /// <summary>
        /// Register the function to be called to create a bot along with configuration settings.
        /// </summary>
        /// <param name="makeCallingBot"> The factory method to make the real time media calling bot.</param>
        /// <param name="realTimeBotServiceSettings"> Configuration settings for the real time media calling bot.</param>
        public static void RegisterRealTimeMediaCallingBot(Func<IRealTimeMediaCallService, IRealTimeMediaCall> makeCallingBot, IRealTimeMediaCallServiceSettings realTimeBotServiceSettings)
        {
            Trace.TraceInformation($"Registering real-time media calling bot");
            if(realTimeBotServiceSettings.CallbackUrl == null)
            {
                throw new ArgumentNullException("callbackUrl");
            }

            if (realTimeBotServiceSettings.NotificationUrl == null)
            {
                throw new ArgumentNullException("notificationUrl");
            }

            RealTimeMediaCallingModule_MakeBot.Register(Container, makeCallingBot, realTimeBotServiceSettings);
        }

        /// <summary>
        /// Process an incoming request
        /// </summary>
        /// <param name="toBot"> The calling request sent to the bot.</param>
        /// <param name="callRequestType"> The type of calling request.</param>
        /// <returns> The response from the bot.</returns>
        public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage toBot, RealTimeMediaCallRequestType callRequestType)
        {
            using (var scope = RealTimeMediaCallingModule.BeginLifetimeScope(Container, toBot))
            {                
                var context = scope.Resolve<RealTimeMediaCallingContext>();
                var parsedRequest = await context.ProcessRequest(callRequestType).ConfigureAwait(false);
               
                if (parsedRequest.Faulted())
                {
                    return GetResponseMessage(parsedRequest.ParseStatusCode, parsedRequest.Content);
                }
                else
                {
                    try
                    {
                        ResponseResult result;
                        var callingBotService = scope.Resolve<IRealTimeCallProcessor>();
                        switch (callRequestType)
                        {
                            case RealTimeMediaCallRequestType.IncomingCall:
                                result = await callingBotService.ProcessIncomingCallAsync(parsedRequest.Content, parsedRequest.SkypeChainId).ConfigureAwait(false);
                                break;

                            case RealTimeMediaCallRequestType.CallingEvent:
                                result = await callingBotService.ProcessCallbackAsync(parsedRequest.Content).ConfigureAwait(false);
                                break;

                            case RealTimeMediaCallRequestType.NotificationEvent:
                                result = await callingBotService.ProcessNotificationAsync(parsedRequest.Content).ConfigureAwait(false);
                                break;

                            default:
                                result = new ResponseResult(ResponseType.BadRequest, $"Unsupported call request type: {callRequestType}");
                                break;
                        }

                        return GetHttpResponseForResult(result);
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError($"RealTimeMediaCallingConversation: {e}");
                        return GetResponseMessage(HttpStatusCode.InternalServerError, e.ToString());
                    }
                }
            }
        }

        private static HttpResponseMessage GetResponseMessage(HttpStatusCode statusCode, string content)
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(statusCode);
            if (!string.IsNullOrEmpty(content))
            {
                responseMessage.Content = new StringContent(content);
            }
            return responseMessage;
        }

        private static HttpResponseMessage GetHttpResponseForResult(ResponseResult result)
        {
            HttpResponseMessage responseMessage;
            switch(result.ResponseType)
            {
                case ResponseType.Accepted:
                    responseMessage = new HttpResponseMessage(HttpStatusCode.Accepted);
                    break;
                case ResponseType.BadRequest:
                    responseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest);
                    break;
                case ResponseType.NotFound:
                    responseMessage = new HttpResponseMessage(HttpStatusCode.NotFound);
                    break;
                default:
                    responseMessage = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    break;
            }

            if (!string.IsNullOrEmpty(result.Content))
            {
                responseMessage.Content = new StringContent(result.Content);
            }
            return responseMessage;
        }
    }   
}
