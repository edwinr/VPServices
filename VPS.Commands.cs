﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using VP;

namespace VPServices
{
    public partial class VPServices : IDisposable
    {
        /// <summary>
        /// Global list of all commands registered
        /// </summary>
        public SortedSet<Command> Commands = new SortedSet<Command>();

        /// <summary>
        /// Sets up event handlers for command parsing and chat printing to console
        /// </summary>
        public void SetupCommands()
        {
            Bot.Chat += parseCommand;
            Bot.Chat += (s, c) =>
            {
                TConsole.WriteLineColored(ConsoleColor.White, " {0} | {1}", c.Name.PadRight(16), c.Message);
            };

            Bot.Console += (s, c) =>
            {
                TConsole.WriteLineColored(ConsoleColor.White, "CONSOLE: {0} {1}", c.Name, c.Message);
            };
        }

        /// <summary>
        /// Parses incoming chat for a command and runs it
        /// </summary>
        void parseCommand(Instance sender, ChatMessage chat)
        {
            // Accept only commands
            if ( !chat.Message.StartsWith("!") )
                return;

            var intercept = chat.Message
                .Trim()
                .Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);

            var beginTime     = DateTime.Now;
            var user          = GetUser(chat.Session);
            var targetCommand = intercept[0].Substring(1).ToLower();
            var data          =
                intercept.Length == 2
                ? intercept[1].Trim()
                : "";

            if (user == null)
                return;

            // Iterate through commands, rejecting invokes if time limited
            foreach (var cmd in Commands)
                if ( TRegex.IsMatch(targetCommand, cmd.Regex) )
                {
                    var timeSpan = cmd.LastInvoked.SecondsToNow();
                    if (timeSpan < cmd.TimeLimit)
                    {
                        App.Warn(chat.Session, "That command was used too recently; try again in {0} seconds.", cmd.TimeLimit - timeSpan);
                        Log.Info("Commands", "User {0} tried to invoke {1} too soon", user.Name, cmd.Name);
                    }
                    else
                    {
                        try
                        {
                            Log.Fine("Commands", "User {0} firing command {1}", user.Name, cmd.Name);
                            cmd.LastInvoked = DateTime.Now;
                            
                            var success = cmd.Handler(this, user, data);
                            if (!success)
                            {
                                App.Warn(chat.Session, "Invalid command use; please see example:");
                                Bot.ConsoleMessage(chat.Session, ChatEffect.Italic, ColorWarn, "", cmd.Example);
                            }
                        }
                        catch (Exception e)
                        {
                            App.Alert(chat.Session, "Sorry, I ran into an issue executing that command. Please notify the host.");
                            App.Alert(chat.Session, "Error: {0}", e.Message);

                            Log.Severe("Commands", "Exception firing command {0}", cmd.Name);
                            e.LogFullStackTrace();
                        }
                    }

                    Log.Fine("Commands", "Command {0} took {1} seconds to process", cmd.Name, beginTime.SecondsToNow());
                    return;
                }

            App.Warn(chat.Session, "Invalid command; try !help");
            Log.Debug("Commands", "Unknown: {0}", targetCommand);
            return;
        }
    }

    public delegate bool CommandHandler(VPServices app, Avatar who, string data);

    /// <summary>
    /// Defines a text command, fired by !(regex)
    /// </summary>
    public class Command : IComparable<Command>
    {
        /// <summary>
        /// Canonical command name
        /// </summary>
        public string Name;
        /// <summary>
        /// Regex pattern that matches this command
        /// </summary>
        public string Regex;
        /// <summary>
        /// Handler to call when this command is invoked
        /// </summary>
        public CommandHandler Handler;
        /// <summary>
        /// Help string for this command
        /// </summary>
        public string Help;
        /// <summary>
        /// Example string for this command
        /// </summary>
        public string Example;
        /// <summary>
        /// How many seconds after invoking is this command disabled
        /// </summary>
        public int TimeLimit;
        /// <summary>
        /// Timestamp command was last invoked
        /// </summary>
        public DateTime LastInvoked = DateTime.Now.AddSeconds(-9001);

        public Command(string name, string rgx, CommandHandler handler, string help, string example = "", int timeLimit = 0)
        {
            Name      = name;
            Regex     = rgx;
            Handler   = handler;
            Help      = help;
            Example   = example;
            TimeLimit = timeLimit;
        }

        public int CompareTo(Command other) { return this.Name.CompareTo(other.Name); }
    }
}
