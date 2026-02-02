// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Colors.Net;
using GitHub.Copilot.SDK;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.ChatActions
{
    [Action(Name = "chat", HelpText = "Start an interactive chat session with an Azure Functions AI agent.")]
    internal class ChatAction : BaseAction
    {
        public override async Task RunAsync()
        {
            ColoredConsole.WriteLine(TitleColor("Welcome to Azure Functions Chat!"));
            ColoredConsole.WriteLine("I can help you create new Function Apps or troubleshoot deployed ones.");
            ColoredConsole.WriteLine("Type 'exit' or 'quit' to end the session.");
            ColoredConsole.WriteLine();

            ColoredConsole.Write(QuestionColor("How can I help you? "));
            var initialPrompt = Console.ReadLine()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(initialPrompt) ||
                initialPrompt.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                initialPrompt.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                ColoredConsole.WriteLine(AdditionalInfoColor("Goodbye!"));
                return;
            }

            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine(AdditionalInfoColor("Connecting to GitHub Copilot..."));
            ColoredConsole.WriteLine();

            try
            {
                await using var client = new CopilotClient();

                await using var session = await client.CreateSessionAsync(new SessionConfig
                {
                    Model = "claude-opus-4.5",
                    Streaming = true,
                    Tools = [],
                    SystemMessage = new SystemMessageConfig
                    {
                        Content = ChatAgentInstructions.SystemPrompt
                    },
                    McpServers = new Dictionary<string, object>
                    {
                        ["manvir-templates"] = new McpLocalServerConfig
                        {
                            Type = "local",
                            Command = "npx",
                            Args = ["-y", "manvir-templates-mcp-server"],
                            Tools = ["*"],
                        },
                    },
                });

                // Set up event handlers
                session.On(ev =>
                {
                    if (ev is AssistantMessageDeltaEvent deltaEvent)
                    {
                        Console.Write(deltaEvent.Data.DeltaContent);
                    }

                    if (ev is SessionIdleEvent)
                    {
                        Console.WriteLine();
                    }

                    if (ev is ToolExecutionStartEvent toolEvent && StaticSettings.IsDebug)
                    {
                        ColoredConsole.WriteLine();
                        ColoredConsole.WriteLine(AdditionalInfoColor($"[Executing: {toolEvent.Data.ToolName}]"));
                        var args = toolEvent.Data.Arguments as JsonElement?;
                        if (args.HasValue)
                        {
                            ColoredConsole.WriteLine(VerboseColor(args.Value.ToString()));
                        }
                    }
                });

                // Run the initial prompt
                await session.SendAndWaitAsync(
                    new MessageOptions
                    {
                        Prompt = initialPrompt,
                    },
                    timeout: TimeSpan.FromMinutes(5));

                // Interactive loop
                while (true)
                {
                    ColoredConsole.WriteLine();
                    ColoredConsole.Write(QuestionColor("You: "));
                    var userInput = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(userInput) ||
                        userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        userInput.Equals("quit", StringComparison.OrdinalIgnoreCase))
                    {
                        ColoredConsole.WriteLine(AdditionalInfoColor("Goodbye!"));
                        break;
                    }

                    ColoredConsole.WriteLine();
                    await session.SendAndWaitAsync(
                        new MessageOptions
                        {
                            Prompt = userInput,
                        },
                        timeout: TimeSpan.FromMinutes(5));
                }
            }
            catch (Exception ex)
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Error communicating with Copilot: {ex.Message}"));
            }
        }
    }
}
