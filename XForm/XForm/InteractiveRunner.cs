﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis.Elfie.Extensions;

using XForm.Data;
using XForm.Extensions;

namespace XForm
{
    public class InteractiveRunner
    {
        private static string s_commandCachePath;
        private IDataBatchEnumerator _pipeline;
        private List<IDataBatchEnumerator> _stages;
        private List<string> _commands;

        public InteractiveRunner()
        {
            s_commandCachePath = Environment.ExpandEnvironmentVariables(@"%TEMP%\XForm.Last.xql");

            _pipeline = null;
            _stages = new List<IDataBatchEnumerator>();
            _commands = new List<string>();
        }

        public int Run()
        {
            int lastCount = 0;

            try
            {
                while (true)
                {
                    Console.Write("> ");

                    // Read the next query line
                    string nextLine = Console.ReadLine();
                    List<string> arguments = PipelineFactory.SplitConfigurationLine(nextLine);
                    if (arguments.Count == 0) return lastCount;

                    string command = arguments[0].ToLowerInvariant();
                    switch (command)
                    {
                        case "quit":
                        case "exit":
                            // Stop on empty, "quit", or "exit"
                            return lastCount;


                        case "back":
                        case "undo":
                            // Unwrap on "back" or "undo"
                            IDataBatchEnumerator last = _stages.LastOrDefault();
                            if (last != null)
                            {
                                _pipeline = last;
                                _stages.RemoveAt(_stages.Count - 1);
                                _commands.RemoveAt(_commands.Count - 1);
                            }

                            break;
                        case "save":
                            if (arguments.Count != 2)
                            {
                                Console.WriteLine($"Usage: 'save' [scriptFilePath]");
                                continue;
                            }
                            else
                            {
                                SaveScript(arguments[1]);
                                Console.WriteLine($"Script saved to \"{arguments[1]}\".");
                                continue;
                            }
                        case "run":
                            if (arguments.Count != 2)
                            {
                                Console.WriteLine($"Usage: 'run' [scriptFilePath]");
                                continue;
                            }
                            else
                            {
                                LoadScript(arguments[1]);
                                break;
                            }
                        case "rerun":
                            LoadScript(s_commandCachePath);
                            break;
                        default:
                            try
                            {
                                _pipeline = AddStage(nextLine);
                                break;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error: {ex.Message}");
                                continue;
                            }
                    }

                    SaveScript(s_commandCachePath);
                    Stopwatch w = Stopwatch.StartNew();

                    // Get the first 10 results
                    IDataBatchEnumerator firstTenWrapper = _pipeline;
                    firstTenWrapper = PipelineFactory.BuildStage("limit 10", firstTenWrapper);
                    firstTenWrapper = PipelineFactory.BuildStage("write cout", firstTenWrapper);
                    lastCount = firstTenWrapper.Run();

                    // Get the count
                    lastCount += _pipeline.Run();
                    firstTenWrapper.Reset();

                    Console.WriteLine();
                    Console.WriteLine($"{lastCount:n0} rows in {w.Elapsed.ToFriendlyString()}.");
                    Console.WriteLine();
                }
            }
            finally
            {
                if (_pipeline != null) _pipeline.Dispose();
            }
        }

        private void LoadScript(string path)
        {
            foreach (string line in File.ReadAllLines(path))
            {
                Console.WriteLine(line);
                _pipeline = AddStage(line);
            }
        }

        private void SaveScript(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllLines(path, _commands);
        }

        private IDataBatchEnumerator AddStage(string nextLine)
        {
            // Save stages before the last one
            _stages.Add(_pipeline);

            // Build the new stage
            _pipeline = PipelineFactory.BuildStage(nextLine, _pipeline);

            // Save the current command set
            _commands.Add(nextLine);
            return _pipeline;
        }
    }
}