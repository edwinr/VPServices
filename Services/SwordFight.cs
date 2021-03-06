﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VP;

namespace VPServices.Services
{
    public class SwordFight : IService
    {
        const string msgReminder   = "PvP swordfight mode is still enabled for you";
        const string msgToggle     = "PvP swordfight mode has been {0} for {1}";
        const string msgTooSoon    = "It is too soon for you to switch PVP status ({0} seconds left)";
        const string msgPunchbag   = "I am at your location; spam me with clicks to attack";
        const string msgHealth     = "Your health is {0} points";
        const string msgKill       = "Rest in peace, {0}. {1} gains 5 health.";
        const string keyLastSwitch = "SwordFightToggle";
        const string keyHealth     = "SwordFightHealth";
        const string keyDeath      = "SwordFightLastDeath";
        const string keyMode       = "SwordFight";

        VPServices         app;
        List<VPObject> spawned = new List<VPObject>();

        public string Name { get { return "Sword fight"; } }
        public void Init(VPServices app, Instance bot)
        {
            this.app = app;

            app.Commands.AddRange( new[] {
                new Command
                (
                    "Swordfight: Toggle", "^swordfight", cmdTogglePVP,
                    @"Toggles or sets swordfighting (PVP) mode for user",
                    @"!swordfight `[true|false]`"
                ),

                new Command
                (
                    "Swordfight: Punchbag", "^punchbag", cmdPunchbag,
                    @"Brings me to user's location to practise swordfighting",
                    @"!punchbag", 5
                ),

                new Command
                (
                    "Swordfight: Health", "^health", cmdHealth,
                    @"Notifys the user of their health",
                    @"!health"
                )
            });

            app.Bot.Property.CallbackObjectCreate += onCreate;
            app.Bot.Avatars.Clicked               += onClick;
            app.Bot.Avatars.Enter += onEnter;
        }

        public void Dispose() { }

        #region Command handlers
        bool cmdTogglePVP(VPServices app, Avatar who, string data)
        {
            var  config = app.GetUserSettings(who);
            bool toggle = false;
            DateTime lastSwitch;
            if ( config.Contains(keyLastSwitch) )
                lastSwitch = DateTime.Parse(config.Get(keyLastSwitch));
            else
                lastSwitch = DateTime.Now.AddSeconds(-60);

            // Reject if too soon
            if ( lastSwitch.SecondsToNow() < 60 )
            {
                var timeLeft = 60 - lastSwitch.SecondsToNow();
                app.Warn(who.Session, msgTooSoon, timeLeft);
                return true;
            }

            // Try to parse user given boolean; silently ignore on failure
            if ( data != "" )
            {
                if ( !VPServices.TryParseBool(data, out toggle) )
                    return false;
            }
            else
                toggle = !config.GetBoolean(keyMode, false);

            // Set new boolean, timeout and if new, health
            config.Set(keyMode, toggle);
            config.Set(keyLastSwitch, DateTime.Now);

            if ( !config.Contains(keyHealth) )
                config.Set(keyHealth, 100);

            var verb = toggle ? "enabled" : "disabled";
            app.NotifyAll(msgToggle, verb, who.Name);
            return true;
        }

        bool cmdPunchbag(VPServices app, Avatar who, string data)
        {
            app.Bot.GoTo(who.X, who.Y, who.Z);
            app.Notify(who.Session, msgPunchbag);

            return true;
        }

        bool cmdHealth(VPServices app, Avatar who, string data)
        {
            var config = app.GetUserSettings(who);
            var health = config.GetInt(keyHealth, 100);

            app.Notify(who.Session, msgHealth, health);
            return true;
        } 
        #endregion

        #region Event handlers
        void onClick(Instance bot, AvatarClick click)
        {
            if ( click.TargetSession == 0 )
                return;

            var source = app.GetUser(click.SourceSession);
            var target = app.GetUser(click.TargetSession);

            if ( source == null )
                return;

            if ( target == null )
            {
                hitBot(source);
                return;
            }

            var sourceConfig = app.GetUserSettings(source);
            var targetConfig = app.GetUserSettings(target);

            if ( !sourceConfig.GetBoolean(keyMode, false) || !targetConfig.GetBoolean(keyMode, false) )
                return;

            var diffX = Math.Abs(source.X - target.X);
            var diffY = Math.Abs(source.Y - target.Y);
            var diffZ = Math.Abs(source.Z - target.Z);

            if ( diffX > .5 || diffY > .4 || diffZ > .5 )
                return;

            if ( cannotHit(source) || cannotHit(target) )
            {
                createHoverText(target.Position, 0, false);
                return;
            }

            var targetHealth = targetConfig.GetInt(keyHealth, 100);
            var critical     = VPServices.Rand.Next(100) <= 10;
            var damage       = VPServices.Rand.Next(5, 25) * ( critical ? 3 : 1 );

            createHoverText(target.Position, damage, critical);
            createBloodSplat(target.Position);

            if ( targetHealth - damage <= 0 )
            {
                app.AlertAll(msgKill, target.Name, source.Name);

                targetConfig.Set(keyDeath, DateTime.Now);
                targetConfig.Set(keyHealth, 100);
                sourceConfig.Set(keyHealth, sourceConfig.GetInt(keyHealth) + 5);
                bot.Avatars.Teleport(click.TargetSession, AvatarPosition.GroundZero);
            }
            else
                targetConfig.Set(keyHealth, targetHealth - damage);
        }

        void onEnter(Instance sender, Avatar avatar)
        {
            var config = app.GetUserSettings(avatar);

            if ( config.GetBoolean(keyMode, false) )
                app.Warn(avatar.Session, msgReminder);
        } 
        #endregion

        #region Attack logic
        void hitBot(Avatar source)
        {
            var critical = VPServices.Rand.Next(100) <= 10;
            var damage   = VPServices.Rand.Next(5, 25) * ( critical ? 3 : 1 );

            createHoverText(app.Bot.Position, damage, critical);
            createBloodSplat(app.Bot.Position);
        }

        bool cannotHit(Avatar who)
        {
            if ( who.X < 1 && who.X > -1 )
                if ( who.Z < 1 && who.Z > -1 )
                    return true;

            DateTime death;
            var      config = app.GetUserSettings(who);

            if ( config.Contains(keyDeath) )
                death = DateTime.Parse(config.Get(keyDeath));
            else
                death = DateTime.Now.AddSeconds(-6);

            if ( death.SecondsToNow() < 5 )
                return true;

            return false;
        } 
        #endregion

        #region UI logic
        void createHoverText(AvatarPosition pos, int damage, bool critical)
        {
            var offsetX     = ( (float) VPServices.Rand.Next(-100, 100) ) / 2000;
            var offsetZ     = ( (float) VPServices.Rand.Next(-100, 100) ) / 2000;
            var description = string.Format("{0}{1}", 0 - damage, critical ? " !!!" : "");
            var color       = damage == 0 ? "blue" : "red";
            var hover       = new VPObject
            {
                Model = "p:fac100x50,s.rwx",
                Rotation = Quaternion.ZeroEuler,
                Action = string.Format("create sign color={0} bcolor=ffff0000 hmargin=20, ambient 1, move 0 2 time=5 wait=10, solid no", color),
                Description = description,
                Position = new Vector3(pos.X + offsetX, pos.Y + .2f, pos.Z + offsetZ)
            };

            app.Bot.Property.AddObject(hover);
            spawned.Add(hover);
        }

        void createBloodSplat(AvatarPosition pos)
        {
            var offsetX     = ( (float) VPServices.Rand.Next(-100, 100) ) / 2000;
            var offsetY     = ( (float) VPServices.Rand.Next(0, 100) ) / 5000;
            var offsetZ     = ( (float) VPServices.Rand.Next(-100, 100) ) / 2000;
            var size        = VPServices.Rand.Next(80, 200);

            var hover       = new VPObject
            {
                Model = "p:flat" + size + ".rwx",
                Rotation = Quaternion.ZeroEuler,
                Action = "create texture bloodsplat1.png, normalmap nmap-puddle1, specularmap smap-puddle1, specular .6 30, solid no",
                Position = new Vector3(pos.X + offsetX, pos.Y + offsetY, pos.Z + offsetZ)
            };

            app.Bot.Property.AddObject(hover);
            spawned.Add(hover);
        }

        void onCreate(Instance bot, ObjectCallbackData obj)
        {
            if ( spawned.Contains(obj.Object) )
                Task.Factory.StartNew(() =>
                {
                    var timer = DateTime.Now;

                    while ( true )
                    {
                        if ( timer.SecondsToNow() > 3 )
                        {
                            bot.Property.DeleteObject(obj.Object);
                            return;
                        }

                        Thread.Sleep(1000);
                    }
                });
        } 
        #endregion
    }
}
