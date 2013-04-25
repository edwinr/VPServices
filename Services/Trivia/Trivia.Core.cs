﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VP;

namespace VPServices.Services
{
    public partial class Trivia : IService
    {
        #region Strings
        const  string   msgAccepted     = "Ding! The answer was {0}, {1} {2}";
        const  string   msgAcceptedFrom = "Ding! The answer was {0} (accepted from {1}), {2} {3}";
        const  string   keyTriviaPoints = "TriviaPoints";
        const  string   tag             = "Trivia";
        static string[] welldones       = new[]
        {
            "well done", "good show", "GG", "nice one", "not bad,", "jolly good", "keep going",
            "^5", "great stuff", "congraduration", "축하 해요", "祝う", "esprit intelligent"
        };
        #endregion
        
        object   mutex         = new object();
        bool     inProgress    = false;
        Task     task;
        DateTime progressSince;
        VPServices   app;

        public string Name { get { return "Trivia"; } }
        public void Init(VPServices app, Instance bot)
        {
            this.app = app;
            addCommands ();
            addWebRoutes();
        }

        public void Dispose()
        {
            lock ( mutex ) { gameEnd(); }
        }

        #region Core game logic
        void gameBegin(TriviaEntry entry)
        {
            app.Bot.ConsoleBroadcast(ChatEffect.Bold, VPServices.ColorInfo, entry.Category + ":", entry.Question);
            app.Bot.Chat     += onChat;
            inProgress        = true;
            progressSince     = DateTime.Now;
            entryInPlay       = entry;
            entryInPlay.Used  = true;

            Log.Debug(tag, "Beginning game with question:\n\t[{0}] {1}\n\tAnswer: {2}",
                entryInPlay.Category, entryInPlay.Question, entryInPlay.Answer);

            task = new Task(gameTimeout);
            task.Start();
        }

        void gameTimeout()
        {
            while ( inProgress )
            {
                if ( progressSince.SecondsToNow() >= 60 )
                    lock ( mutex )
                    {
                        gameEnd();
                        app.NotifyAll("Timeout! The answer was {0}.", entryInPlay.CanonicalAnswer);
                        Log.Debug(tag, "Question timed out");
                    }

                Thread.Sleep(500);
            }
        }

        void gameEnd()
        {
            if ( !inProgress ) return;

            inProgress    = false;
            app.Bot.Chat -= onChat;
            Log.Fine(tag, "Game has ended");
        } 
        #endregion

        #region Event handlers
        void onChat(Instance bot, ChatMessage chat)
        {
            lock ( mutex )
            {
                string[] match;
                string[] wrongMatch;

                if ( !TRegex.TryMatch(chat.Message, entryInPlay.Answer, out match) )
                    return;

                if ( entryInPlay.Wrong != null && TRegex.TryMatch(chat.Message, entryInPlay.Wrong, out wrongMatch) )
                {
                    Log.Debug(tag, "Given answer '{0}' by {1} matched, but turned out to be wrong; rejecting", wrongMatch[0], chat.Name);
                    return;
                }

                gameEnd();

                var welldone = welldones.Skip(VPServices.Rand.Next(welldones.Length)).Take(1).Single();

                if ( match[0].IEquals(entryInPlay.CanonicalAnswer) )
                    app.Bot.ConsoleBroadcast(ChatEffect.Bold, VPServices.ColorInfo, "Triviamaster",
                        msgAccepted, entryInPlay.CanonicalAnswer, welldone, chat.Name);
                else
                    app.Bot.ConsoleBroadcast(ChatEffect.Bold, VPServices.ColorInfo, "Triviamaster",
                        msgAccepted, entryInPlay.CanonicalAnswer, match[0], welldone, chat.Name);

                Log.Debug(tag, "Correct answer '{0}' by {1}", match[0], chat.Name);
                awardPoint(chat.Name);
            }
        } 
        #endregion

        #region Misc logic
        void awardPoint(string who)
        {
            var config = app.GetUserSettings(who);
            var points = config.GetInt(keyTriviaPoints, 0);

            points++;
            config.Set(keyTriviaPoints, points);
            Log.Fine(tag, "{0} is now up to {1} points", who, points);

            if ( points % 10 == 0 )
                app.NotifyAll("{0} is climbing the scoreboard with {1} points!", who, points);
        }

        void skipQuestion()
        {
            if ( task == null ) return;

            Log.Debug(tag, "Skipping question...");
            gameEnd();
            task.Wait();
        } 
        #endregion
    }   
}
