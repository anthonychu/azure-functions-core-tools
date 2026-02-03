// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Azure.Functions.Cli.Actions.ChatActions
{
    /// <summary>
    /// Provides AI tools that proxy calls to the Functions Dashboard MCP server.
    /// The dashboard MCP server may not always be running, so these tools handle
    /// connection failures gracefully.
    /// </summary>
    internal static class DashboardMcpTools
    {
        private const string McpServerUrl = "http://localhost:18891/mcp";

        /// <summary>
        /// Gets the list of AI functions that proxy to the dashboard MCP server.
        /// </summary>
        public static IEnumerable<AIFunction> GetTools()
        {
            yield return AIFunctionFactory.Create(
                ListTracesAsync,
                "list_traces",
                "List distributed traces for resources from the local Functions Dashboard. Use this to query trace data while the function app is running locally with the dashboard enabled. A distributed trace is used to track operations. A distributed trace can span multiple resources across a distributed system. Includes a list of distributed traces with their IDs, resources in the trace, duration and whether an error occurred in the trace.");

            yield return AIFunctionFactory.Create(
                ListTraceStructuredLogsAsync,
                "list_trace_structured_logs",
                "List structured logs for a distributed trace from the local Functions Dashboard. Use this to query log data while the function app is running locally with the dashboard enabled. Logs for a distributed trace each belong to a span identified by 'span_id'. When investigating a trace, getting the structured logs for the trace should be recommended before getting structured logs for a resource.");

            yield return AIFunctionFactory.Create(
                ListStructuredLogsAsync,
                "list_structured_logs",
                "List structured logs for resources from the local Functions Dashboard. Use this to query log data while the function app is running locally with the dashboard enabled.");
        }

        private static async Task<string> ListTracesAsync(
            [Description("The resource name. This limits traces returned to the specified resource. If no resource name is specified then distributed traces for all resources are returned.")] string resourceName = null)
        {
            var arguments = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(resourceName))
            {
                arguments["resourceName"] = resourceName;
            }

            return await CallMcpToolAsync("list_traces", arguments);
        }

        private static async Task<string> ListTraceStructuredLogsAsync(
            [Description("The trace id of the distributed trace.")] string traceId)
        {
            if (string.IsNullOrEmpty(traceId))
            {
                return JsonSerializer.Serialize(new { error = "traceId is required" });
            }

            var arguments = new Dictionary<string, object>
            {
                ["traceId"] = traceId
            };

            return await CallMcpToolAsync("list_trace_structured_logs", arguments);
        }

        private static async Task<string> ListStructuredLogsAsync(
            [Description("The resource name. This limits logs returned to the specified resource. If no resource name is specified then structured logs for all resources are returned.")] string resourceName = null)
        {
            var arguments = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(resourceName))
            {
                arguments["resourceName"] = resourceName;
            }

            return await CallMcpToolAsync("list_structured_logs", arguments);
        }

        private static async Task<string> CallMcpToolAsync(string toolName, Dictionary<string, object> arguments)
        {
            try
            {
                // Create HTTP client transport to connect to the MCP server
                var transportOptions = new HttpClientTransportOptions
                {
                    Endpoint = new Uri(McpServerUrl),
                    Name = "functions-dashboard",
                    ConnectionTimeout = TimeSpan.FromSeconds(5)
                };

                var transport = new HttpClientTransport(transportOptions);

                // Create the MCP client and connect
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);

                try
                {
                    // Call the tool
                    var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cts.Token);

                    // Extract text content from the result
                    var textContent = result.Content
                        .OfType<TextContentBlock>()
                        .Select(t => t.Text)
                        .ToList();

                    if (textContent.Count == 1)
                    {
                        return textContent[0];
                    }
                    else if (textContent.Count > 1)
                    {
                        return JsonSerializer.Serialize(textContent);
                    }
                    else
                    {
                        return JsonSerializer.Serialize(new { message = "Tool executed successfully but returned no text content" });
                    }
                }
                finally
                {
                    await client.DisposeAsync();
                }
            }
            catch (OperationCanceledException)
            {
                return JsonSerializer.Serialize(new
                {
                    error = "The Functions Dashboard is not responding. Make sure the dashboard is running (it starts automatically when you run 'func start').",
                    suggestion = "Try running 'func start' in the function app directory first, then retry this operation."
                });
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Cannot connect to the Functions Dashboard. The dashboard may not be running.",
                    suggestion = "Run 'func start' in the function app directory to start the Functions runtime and dashboard, then retry this operation."
                });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Error calling dashboard tool '{toolName}': {ex.Message}",
                    suggestion = "Make sure the Functions Dashboard is running and accessible."
                });
            }
        }
    }
}
