using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

namespace HordeGamemode
{
    public class Main : Script
    {

        ScriptSettings config;
        Random random = new Random();
        bool isModEnabled = false;

        int MAX_ZOMBIES;
        int AI_TICK_RATE;
        int CHANCE_OF_RUNNER_ZOMBIES;
        float DEBUG_PED_SEARCH_DISTANCE;
        float DEBUG_ZOMBIE_DESPAWN_DISTANCE;
        string ZOMBIE_ANIM;
        bool ENABLE_BLACKOUT;
        bool RUN_ON_STARTUP;
        bool COPS_WILL_ARREST_PLAYER;
        bool IS_IN_ARMY_ZONE = false;
        static RelationshipGroup relationZombies = World.AddRelationshipGroup("ZOMBIES");
        Keys INPUT_START;

        List<Ped> zombies = new List<Ped>();
        List<Ped> runners = new List<Ped>();

        public Main()
        {
            config = ScriptSettings.Load("scripts/" + this.Filename + ".ini");
            ZOMBIE_ANIM = Settings.GetValue<string>("Animations", "ZombieAnim", "move_m@drunk@verydrunk");
            AI_TICK_RATE = Settings.GetValue<int>("Debug", "ZombieTickRate", 60);
            MAX_ZOMBIES = Settings.GetValue<int>("Debug", "MaxZombies", 25);
            ENABLE_BLACKOUT = Settings.GetValue<bool>("Gameplay", "EnableBlackout", false);
            DEBUG_PED_SEARCH_DISTANCE = Settings.GetValue<float>("Debug", "ZombieSearchDistance", 600f);
            DEBUG_ZOMBIE_DESPAWN_DISTANCE = Settings.GetValue<float>("Debug", "ZombieDespawnDistance", 250f);
            CHANCE_OF_RUNNER_ZOMBIES = Settings.GetValue<int>("Gameplay", "RunnerZombieChance", 5);
            COPS_WILL_ARREST_PLAYER = Settings.GetValue<bool>("Gameplay", "CopsWillArrestPlayer", false);
            RUN_ON_STARTUP = Settings.GetValue<bool>("Gameplay", "RunOnStartup", true);
            INPUT_START = Settings.GetValue<Keys>("Input", "INPUT_START", Keys.Y);

            relationZombies.SetRelationshipBetweenGroups(relationZombies, Relationship.Like);
            relationZombies.SetRelationshipBetweenGroups("PLAYER", Relationship.Hate);
            relationZombies.SetRelationshipBetweenGroups("CIVMALE", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("CIVFEMALE", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("COP", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("ARMY", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_LOST", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_BALLAS", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_FAMILY", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_MEXICAN", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_MARABUNTE", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_CULT", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_SALVA", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("AMBIENT_GANG_WEICHENG", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("MEDIC", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("GUARD_DOG", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("SECURITY_GUARD", Relationship.Hate, bidirectionally: true);
            relationZombies.SetRelationshipBetweenGroups("PRIVATE_SECURITY", Relationship.Hate, bidirectionally: true);

            if (RUN_ON_STARTUP == true)
            {
                isModEnabled = true;
                World.Blackout = ENABLE_BLACKOUT;
                Tick += tickUpdate;
            }

            Aborted += onScriptAborted;
            KeyDown += onKeyDown;
            Interval = 0;
        }

        void tickUpdate(object sender, EventArgs e)
        {
            updateZoneState();
            ZombieJanitor();
            pickRandomZombie();
            UpdateZombies();
            SpawnAbandonedCar();
        }

        void onScriptAborted(object sender, EventArgs e)
        {
            Tick -= tickUpdate;
            KeyDown -= onKeyDown;

            World.Blackout = false;
            Game.Player.DispatchsCops = true;
            Game.MaxWantedLevel = 5;

            ZombieJanitor(isForced: true);
        }

        void onKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == INPUT_START)
            {
                if (isModEnabled)
                {
                    isModEnabled = false;
                    if (ENABLE_BLACKOUT)
                    {
                        World.Blackout = false;
                    }
                    Game.Player.DispatchsCops = true;
                    Game.MaxWantedLevel = 5;
                    Tick -= tickUpdate;
                    ZombieJanitor(isForced: true);
                }
                else
                {
                    isModEnabled = true;
                    if (ENABLE_BLACKOUT)
                    {
                        World.Blackout = ENABLE_BLACKOUT;
                    }
                    if (Game.Player.WantedLevel > 0)
                    {
                        Game.Player.WantedLevel = 0;
                    }
                    Tick += tickUpdate;
                }
            }
        }

        bool canDamage(Ped human)
        {
            var playerCanControl = Game.Player.CanControlCharacter ? true : false;

            if (human.IsPlayer && !playerCanControl) return false;
            if (human.IsSittingInVehicle()) return false;
            if (human.IsInvincible) return false;

            return true;
        }

        void UpdateZombies()
        {
            if (zombies.Count > 0)
            {
                for (int i = 0; i < zombies.Count; i++)
                {
                    var zombie = zombies[i];
                    if (zombie != null && zombie.Exists())
                    {
                        zombie.RelationshipGroup = relationZombies.Hash;
                        var human = findHuman(zombie);
                        if (human != null && human.Exists())
                        {
                            if (zombie.Position.DistanceTo(human.Position) < 2f)
                            {
                                if (human.IsPlayer || human.IsPersistent)
                                {
                                    if (canDamage(human) && Game.GameTime % 25 == 0)
                                    {
                                        human.Health -= 25;
                                    }
                                }
                                else
                                {
                                    if (canDamage(human))
                                    {
                                        infectHuman(human);
                                    }
                                }
                            }
                            if (Game.GameTime % AI_TICK_RATE == 0)
                            {
                                if (runners.Contains(zombie) || zombie.Model.IsAnimalPed)
                                {
                                    zombie.Task.RunTo(human.Position, ignorePaths: true);
                                }
                                else
                                {
                                    zombie.Task.GoTo(human.Position);
                                }
                            }
                        }
                    }
                }
            }
        }

        void SpawnAbandonedCar()
        {
            if (Game.GameTime % 500 == 0 && random.Next(0, 100) == 50)
            {
                var veh = World.CreateRandomVehicle(Game.Player.Character.Position.Around(250f), heading: random.Next(0, 180));
                veh.EngineHealth = 0;
                veh.PlaceOnGround();
                veh.PlaceOnNextStreet();
                veh.MarkAsNoLongerNeeded();
            }
        }

        void pickRandomZombie()
        {
            if (IS_IN_ARMY_ZONE) return;
            foreach (Ped ped in World.GetNearbyPeds(Game.Player.Character, DEBUG_PED_SEARCH_DISTANCE))
            {
                if (!ped.IsPlayer && !ped.IsPersistent && ped.IsAlive && !zombies.Contains(ped) && !runners.Contains(ped))
                {
                    if (zombies.Count >= MAX_ZOMBIES)
                    {
                        if (ped.Weapons.BestWeapon.Group == WeaponGroup.Unarmed || ped.Weapons.BestWeapon.Group == WeaponGroup.Melee)
                        {
                            if (!ped.IsFleeing)
                            {
                                if (ped.IsInVehicle() && random.Next(0, 100) < 25)
                                {
                                    ped.Task.LeaveVehicle(flags: LeaveVehicleFlags.LeaveDoorOpen);
                                }
                                else
                                {
                                    ped.Task.ReactAndFlee(Game.Player.Character);
                                }
                            }
                        }
                    }
                    else
                    {
                        infectHuman(ped);
                    }
                }
            }
        }

        Ped findHuman(Ped infected)
        {
            var range = Game.Player.Character.IsInStealthMode ? 15 : 25;
            foreach (Ped ped in World.GetNearbyPeds(infected, range))
            {
                if (ped.IsAlive && !zombies.Contains(ped) && !runners.Contains(ped) && ped.Model.IsHumanPed)
                {
                    return ped;
                }
            }

            return null;
        }

        void infectHuman(Ped ped)
        {
            ped.IsPersistent = true;
            ped.AlwaysKeepTask = true;
            ped.BlockPermanentEvents = true;
            ped.IsEnemy = true;
            ped.CanWrithe = false;
            ped.RelationshipGroup = relationZombies.Hash;

            if (ped.IsSittingInVehicle())
            {
                ped.CurrentVehicle.EngineHealth = 0;
                ped.Task.LeaveVehicle(flags: LeaveVehicleFlags.BailOut);
            }

            ped.Task.ClearAllImmediately();
            ped.Task.WanderAround();

            var chance = Game.TimeScale == 24 ? 15 : CHANCE_OF_RUNNER_ZOMBIES;
            if (random.Next(0, 100) < chance && ped.IsHuman || ped.Model.IsAnimalPed)
            {
                runners.Add(ped);
            }
            else
            {
                if (!Function.Call<bool>(Hash.HAS_CLIP_SET_LOADED, new InputArgument[] { ZOMBIE_ANIM }))
                {
                    Function.Call(Hash.REQUEST_CLIP_SET, new InputArgument[] { ZOMBIE_ANIM });
                }

                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, new InputArgument[] { ped.Handle, ZOMBIE_ANIM, 1048576000 });
            }

            Function.Call(Hash.STOP_PED_SPEAKING, new InputArgument[] { ped.Handle, true });
            Function.Call(Hash.DISABLE_PED_PAIN_AUDIO, new InputArgument[] { ped.Handle, true });

            //Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, 1);
            Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, 0);
            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, 1);
            Function.Call(Hash.APPLY_PED_DAMAGE_PACK, ped, "BigHitByVehicle", 0.0, 9.0);
            Function.Call(Hash.APPLY_PED_DAMAGE_PACK, ped, "SCR_Dumpster", 0.0, 9.0);
            Function.Call(Hash.APPLY_PED_DAMAGE_PACK, ped, "SCR_Torture", 0.0, 9.0);

            zombies.Add(ped);
        }

        void ZombieJanitor(bool isForced = false)
        {
            if (zombies.Count > 0)
            {
                for (int i = zombies.Count - 1; i > -1; i--)
                {
                    var ped = zombies[i];
                    if (ped != null && ped.Exists())
                    {
                        if (isForced)
                        {
                            if (ped.IsAlive) { ped.Kill(); };
                            RemoveRunner(ped);
                            ped.MarkAsNoLongerNeeded();
                            zombies.RemoveAt(i);
                        }
                        else
                        {
                            if (ped.IsDead || Game.Player.IsDead || World.GetDistance(ped.Position, Game.Player.Character.Position) > DEBUG_ZOMBIE_DESPAWN_DISTANCE)
                            {
                                RemoveRunner(ped);
                                ped.MarkAsNoLongerNeeded();
                                zombies.RemoveAt(i);
                            }
                        }
                    }
                }
            }
        }

        void RemoveRunner(Ped ped)
        {
            var runnerIndex = runners.IndexOf(ped);
            if (runnerIndex != -1)
            {
                runners.RemoveAt(runnerIndex);
            }
        }

        bool isInMilitaryBase()
        {
            if (Function.Call<bool>(Hash.IS_ENTITY_IN_ZONE, Game.Player.Character, "ArmyB") || Function.Call<bool>(Hash.IS_ENTITY_IN_ZONE, Game.Player.Character, "Zancudo"))
            {
                return true;
            }

            return false;
        }

        void updateZoneState()
        {
            if (isInMilitaryBase())
            {
                IS_IN_ARMY_ZONE = true;
                Game.Player.DispatchsCops = true;
                Game.MaxWantedLevel = 5;
            }
            else
            {
                IS_IN_ARMY_ZONE = false;
                Game.Player.DispatchsCops = false;
                Game.MaxWantedLevel = COPS_WILL_ARREST_PLAYER ? 1 : 0;
            }
        }

    }
}