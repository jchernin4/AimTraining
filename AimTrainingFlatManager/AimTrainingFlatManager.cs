using Oxide.Core;
using Oxide.Core.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Oxide.Plugins {
	[Info("Aim Training Flat Manager", "Fyre", "0.0.1")]
	public class AimTrainingFlatManager : RustPlugin {

		/**
		 * For 1v1 ranked scrims, to make it simple have 1 arena where 2 people are fighting and have the rest spectating
		 * (maybe dont let them spectate so they can't give callouts on discord, or have no spectators at all)
		 * 
		 * Need a better way of finding what game and what team a player is on
		 * (maybe a new class that contains BasePlayer with game and team information, make a list of this object and then have
		 * a method to find the object in the list by the BasePlayer for when a hook only gives BasePlayer)
		 * then change spectating instead of its own team to a flag (isSpectating) and then maybe make another (isOnlySpectating) if they wish to stay spectating and not join a team (or could just keep team null)
		 * Need a way to differentiate between flats, gamemodes, etc. but also need the player to be able to be in no games (isInGame or something?) with flatID and team null for example
		 * 
		 * Need to keep this on its own so it doesn't interfere with other plugins (this should just work for people in flats, and ignore everyone else) ^
		 * 
		 * TODO: I actually like that idea ^
		 */

		private List<Flat> flats;

		#region Oxide Hooks
		void Init() {
			Utils.initFlatConfig();
			flats = new List<Flat>();
		}

		// Moves players back to their team's spawn when they are too far away from it
		object OnPlayerTick(BasePlayer player, PlayerTick msg, bool wasPlayerStalled) {
			foreach (Flat flat in flats) {
				FlatPlayer flatPlayer = flat.FindFlatPlayer(player);

				if (flatPlayer != null && !flat.isStarted) {
					Vector3 curPos = flatPlayer.basePlayer.ServerPosition;
					// TODO: Test this: Vector3 diff = g.GetSpawn(side) - curPos; and compare this instead (check if Math.abs(diff.x) or Math.abs(diff.z) is greater than 9)
					if (Math.Abs(curPos.x - flat.GetSpawn(flatPlayer.team).x) > 9 || Math.Abs(curPos.z - flat.GetSpawn(flatPlayer.team).z) > 9) {
						player.Teleport(flat.GetSpawn(flatPlayer.team));
					}

					break;
				}
			}

			return null;
		}

		/**
		 * Stop ingame players from taking damage unless the game they are in has started or the attacker is on the same team
		 */
		object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info) {
			// Ignore entitys taking damage that aren't players to prevent errors on casting
			if (info.HitEntity.GetType() != typeof(BasePlayer)) {
				return null;
			}

			BasePlayer attacker = info.InitiatorPlayer;
			BasePlayer victim = (BasePlayer)info.HitEntity;

			// Allow the player to kill themselves
			if (attacker.Equals(victim)) {
				return null;
			}

			bool sameTeam = false;
			FlatPlayer attackerPlayer = null;
			FlatPlayer victimPlayer = null;
			foreach (Flat flat in flats) {
				// TODO: Theres got to be a better way of doing this (new player design should make this easier)
				attackerPlayer = flat.FindFlatPlayer(attacker);
				victimPlayer = flat.FindFlatPlayer(victim);
				if (attackerPlayer != null && victimPlayer != null) {
					break;
				}
			}
			if (attackerPlayer != null && victimPlayer != null && attackerPlayer.team == victimPlayer.team) {
				sameTeam = true;
			}

			// TODO: Remove "|| sameTeam" or add a flag (!isFriendlyFireEnabled && sameTeam) to enable friendly fire
			if (attackerPlayer != null && (!attackerPlayer.flat.isStarted || sameTeam)) {
				info.damageTypes.ScaleAll(0);
			}
			return null;
		}

		/*
		 * If player is going to die, instead of killing them have them heal and teleport to spectator spawn
		 */
		object OnPlayerDeath(BasePlayer victim, HitInfo info) {
			/* 
			 * Let the player die if they are disconnected
			 * TODO: Uncomment so it doesn't block death of disconnected players. Commented because bots aren't "connected" to the server so they will also be allowed to be fully killed
			 */
			/*if (!player.IsConnected) {
				return null;
			}*/

			BasePlayer killer = info.InitiatorPlayer;
			FlatPlayer killerPlayer = null;
			FlatPlayer victimPlayer = null;

			foreach (Flat flat in flats) {
				killerPlayer = flat.FindFlatPlayer(killer);
				victimPlayer = flat.FindFlatPlayer(victim);
				if (killerPlayer != null) {
					break;
				}
			}

			if (killerPlayer != null) {
				killerPlayer.flat.OnPlayerKilled(victimPlayer, info, rust);

			} else {
				// This shouldn't happen (the killer wasn't in a game)
				victimPlayer.basePlayer.StopWounded();
				victimPlayer.basePlayer.Heal(100);
				victimPlayer.basePlayer.Teleport(Utils.worldSpawn);
			}

			// Returning anything other than null overwrites default behavior (stops them from dying)
			return 0;
		}

		void OnPlayerDisconnected(BasePlayer player) {
			// Killing player -> OnPlayerDeath hook, OnPlayerDeath lets the player die -> Flat.OnPlayerLeave should remove player from flat/end round if necessary
			player.Kill();
			foreach (Flat flat in flats) {
				FlatPlayer flatPlayer = flat.FindFlatPlayer(player);
				// Only checking A and B teams
				if (flatPlayer != null && flat.GetTeamPlayers(FlatTeam.A).Concat(flat.GetTeamPlayers(FlatTeam.B)).ToList().Contains(flatPlayer)) {
					flat.OnPlayerLeave(flatPlayer);
				}
			}
		}

		// Spawn at spawn
		object OnPlayerRespawn(BasePlayer player) {
			return new BasePlayer.SpawnPoint() { pos = Utils.worldSpawn, rot = new UnityEngine.Quaternion(0, 0, 0, 1) };
		}

		void OnEntitySpawned(BaseNetworkable entity) {
			string[] deletePrefabs = new string[] { "hotairballoon.prefab", "stone-collectable.prefab", "testridablehorse.prefab", "horse.corpse.prefab" };

			// TODO: Change once you get a test account, used for testing with fake players spawned through console
			if (entity.name.EndsWith("player.prefab")) {
				JoinFlat((BasePlayer)entity, "flat", new string[] { "0" });
			}

			if (deletePrefabs.Contains(entity.PrefabName)) {
				timer.Once(1, () => {
					entity.Kill();
				});
			}
		}
		#endregion

		#region Commands

		[ChatCommand("flat")]
		// TODO: If flat is full, send player to spectators, and send them back again if they try to join from spectator to a team
		void JoinFlat(BasePlayer player, string command, string[] args) {
			if (args.Length == 1 && int.Parse(args[0]) < 9) {
				bool flatExists = false;
				Flat runningFlat = null;

				foreach (Flat flat in flats) {
					FlatPlayer flatPlayer = flat.FindFlatPlayer(player);
					if (flatPlayer != null) {
						rust.SendChatMessage(player, "", "You are already in a flat. Type /leave to leave.", player.UserIDString);
						return;
					}
				}
				// Need to be separate so all flats can be searched
				foreach (Flat flat in flats) {
					if (flat.flatID == int.Parse(args[0])) {
						flatExists = true;
						runningFlat = flat;
						break;
					}
				}

				if (flatExists) {
					FlatPlayer newFlatPlayer;
					if (runningFlat.GetTeamPlayers(FlatTeam.A).Count > runningFlat.GetTeamPlayers(FlatTeam.B).Count) {
						newFlatPlayer = new FlatPlayer(player, runningFlat, false, true, FlatTeam.B);
						runningFlat.OnPlayerJoinTeam(newFlatPlayer);
					} else {
						newFlatPlayer = new FlatPlayer(player, runningFlat, false, true, FlatTeam.A);
						runningFlat.OnPlayerJoinTeam(newFlatPlayer);
					}

					/*
					 * If the game object was created but there aren't any active players as leader, leadership is transferred to the connecting player.
					 * Chances are, this will happen when there is nobody in the arena
					 */
					if (!runningFlat.GetTeamPlayers(FlatTeam.A).Concat(runningFlat.GetTeamPlayers(FlatTeam.B)).Contains(runningFlat.leader)) {
						runningFlat.OnLeaderUpdate(newFlatPlayer);
					}

				} else {
					runningFlat = new Flat(rust);
					runningFlat.flatID = int.Parse(args[0]);
					// Don't call OnLeaderUpdate here because its the first time the flat is being created
					FlatPlayer newFlatPlayer = new FlatPlayer(player, runningFlat, true, false, FlatTeam.A);
					runningFlat.leader = newFlatPlayer;
					flats.Add(runningFlat);

					runningFlat.OnPlayerJoinTeam(newFlatPlayer);
				}

				rust.SendChatMessage(player, "", "Sending you to flat " + args[0]);
			}
		}

		[ChatCommand("leave")]
		void Leave(BasePlayer player, string command, string[] args) {
			foreach (Flat flat in flats) {
				FlatPlayer flatPlayer = flat.FindFlatPlayer(player);

				if (flatPlayer != null) {
					if (flat.isStarted) {
						rust.SendChatMessage(player, "", "You may not leave a flat that has already started.");

					} else {
						flat.OnPlayerLeave(flatPlayer);
					}

					return;
				}
			}
			rust.SendChatMessage(player, "", "You are not in a flat.");
		}

		[ChatCommand("spawn")]
		void TeleportToSpawn(BasePlayer player, string command, string[] args) {
			player.Teleport(Utils.worldSpawn);
		}

		[ChatCommand("start")]
		void StartFlat(BasePlayer player, string command, string[] args) {

			foreach (Flat flat in flats) {
				FlatPlayer flatPlayer = flat.FindFlatPlayer(player);

				if (flatPlayer != null) {
					if (flatPlayer.isLeader) {
						if (!flat.isStarted) {
							if (flat.GetTeamPlayers(FlatTeam.A).Count > 0 && flat.GetTeamPlayers(FlatTeam.B).Count > 0) {
								flat.StartRound();
								foreach (FlatPlayer p in flat.players) {
									rust.SendChatMessage(p.basePlayer, "", "A round has started!");
								}

								return;

							} else {
								rust.SendChatMessage(player, "", "There are not enough players on this flat to start.");
							}

						} else {
							rust.SendChatMessage(player, "", "A round has already started.");
						}

					} else {
						rust.SendChatMessage(player, "", "You are not this flat's leader. Ask " + flat.leader.basePlayer.displayName + " to type /start");
					}
					return;
				}
			}

			rust.SendChatMessage(player, "", "You are not in a flat.");
		}

		#endregion
	}


	public class Flat {
		public int flatID;
		public FlatPlayer leader;
		public List<FlatPlayer> players;

		private Game.Rust.Libraries.Rust rust;

		public List<string> kitAttire;
		public List<string> kitWeapons;
		public List<string> kitAmmo;
		public Dictionary<string, int> kitMeds;

		public bool isStarted;

		public Flat(Game.Rust.Libraries.Rust rust) {
			players = new List<FlatPlayer>();
			kitAttire = new List<string>();
			kitWeapons = new List<string>();
			kitAmmo = new List<string>();
			kitMeds = new Dictionary<string, int>(); // Allow choosing of how many of each med to give (key is med type, int is number to give)

			this.rust = rust;

			// TODO: Allow changing kits
			kitAttire.Add("metal.facemask");
			kitAttire.Add("metal.plate.torso");
			kitAttire.Add("roadsign.kilt");
			kitAttire.Add("hoodie");
			kitAttire.Add("shoes.boots");
			kitAttire.Add("pants");
			kitAttire.Add("tactical.gloves");

			kitWeapons.Add("rifle.ak");

			kitAmmo.Add("ammo.rifle");

			kitMeds["syringe.medical"] = 12;
			kitMeds["bandage"] = 12;

			isStarted = false;
		}

		#region Rounds
		public void StartRound() {
			isStarted = true;

			foreach (FlatPlayer p in players) {
				if (p.team == FlatTeam.A || p.team == FlatTeam.B) {
					HealAndGiveKit(p);
				}
			}
			// Allow movement/placing walls/damage/etc. (Movement is controlled in OnPlayerTick)
		}

		public void EndRound() {
			int aliveAPlayers = 0;
			int aliveBPlayers = 0;

			foreach (FlatPlayer p in players) {
				if (!p.isSpectating) {
					if (p.team == FlatTeam.A) {
						aliveAPlayers++;
					} else if (p.team == FlatTeam.B) {
						aliveBPlayers++;
					}
				}

			}
			FlatTeam winner = FlatTeam.Spectator;
			if (aliveAPlayers == 0) {
				winner = FlatTeam.B;

			} else if (aliveBPlayers == 0) {
				winner = FlatTeam.A;
			}

			foreach (FlatPlayer p in players) {
				rust.SendChatMessage(p.basePlayer, "", "Team " + winner + " won the round!");

				if (p.isSpectating) {
					if (p.team == FlatTeam.A || p.team == FlatTeam.B) {
						// Keeping inside if so that people that aren't on a team stay as spectators
						p.isSpectating = false;
					}
				}
			}

			foreach (FlatPlayer p in players) {
				HealAndGiveKit(p);
				p.basePlayer.Teleport(GetSpawn(p.team));
			}

			isStarted = false;
			// Destroy placed walls, stop movement out of spawn, stop allowing placing of walls, stop damage, etc.
		}
		#endregion

		#region Events
		public void OnPlayerKilled(FlatPlayer victim, HitInfo info, Game.Rust.Libraries.Rust rust) {
			// Send kill message
			BasePlayer killer = info.InitiatorPlayer;
			FlatPlayer killerPlayer = null;

			foreach (FlatPlayer p in players) {
				if (p.basePlayer.Equals(killer)) {
					killerPlayer = p;
				}
			}

			string weaponName;
			int distance;
			try {
				weaponName = info.Weapon.LookupPrefab().name;
				distance = (int)info.ProjectileDistance;
			} catch {
				weaponName = "suicide";
				distance = 0;
			}

			foreach (FlatPlayer p in players) {
				rust.SendChatMessage(p.basePlayer, "", GetKillMessage(p, killerPlayer, victim, weaponName, distance));
			}

			Utils.FullHeal(victim.basePlayer);
			victim.basePlayer.inventory.Strip();
			victim.isSpectating = true;
			victim.basePlayer.Teleport(GetSpecSpawn());

			// If the game is finished
			if (GetTeamPlayers(FlatTeam.A).Count == 0 || GetTeamPlayers(FlatTeam.B).Count == 0) {
				EndRound();
			}
		}

		public void OnPlayerJoinTeam(FlatPlayer player) {
			Utils.FullHeal(player.basePlayer);

			// Check if they just want to join spectators and not A/B
			if (player.team == FlatTeam.Spectator) {
				player.basePlayer.Teleport(GetSpecSpawn());
				return;
			}

			// If the game isn't started, add player to team, else put them in spectators with the side they want to join saved for next round
			if (!isStarted) {
				HealAndGiveKit(player);
				player.basePlayer.Teleport(GetSpawn(player.team));

			} else {
				player.isSpectating = true;
				player.basePlayer.Teleport(GetSpecSpawn());
			}

			string message = "<color=#02f0e8>" + player.basePlayer.displayName + "</color> has joined team " + player.team.ToString().ToUpper();
			foreach (FlatPlayer p in players) {
				rust.SendChatMessage(p.basePlayer, "", message);
			}

			players.Add(player);
		}

		// TODO: If a player leaves, the game won't end, so you should make it as if they died
		public void OnPlayerLeave(FlatPlayer leftPlayer) {
			List<FlatPlayer> activePlayers = GetTeamPlayers(FlatTeam.A).Concat(GetTeamPlayers(FlatTeam.B)).ToList();
			int aPlayers = GetTeamPlayers(FlatTeam.A).Count;
			int bPlayers = GetTeamPlayers(FlatTeam.B).Count;

			players.Remove(leftPlayer);

			// Check if they are connected, since this is called when players leave the server too
			if (leftPlayer.basePlayer.IsConnected) {
				leftPlayer.basePlayer.metabolism.calories.value = 500;
				leftPlayer.basePlayer.metabolism.hydration.value = 250;
				leftPlayer.basePlayer.metabolism.SendChangesToClient();

				leftPlayer.basePlayer.inventory.Strip();
				Utils.FullHeal(leftPlayer.basePlayer);

				leftPlayer.basePlayer.EnsureDismounted();
				if (leftPlayer.basePlayer.HasParent()) {
					leftPlayer.basePlayer.SetParent(null, true, true);
				}

				leftPlayer.basePlayer.EndLooting();
				leftPlayer.basePlayer.StartSleeping();
				leftPlayer.basePlayer.RemoveFromTriggers();

				leftPlayer.basePlayer.Teleport(Utils.worldSpawn);
			}

			if (leader.Equals(leftPlayer) && activePlayers.Count > 0) {
				int index = new System.Random().Next(activePlayers.Count);
				leader = activePlayers[index];
				OnLeaderUpdate(leader);
			}

			if (aPlayers == 0 || bPlayers == 0) {
				EndRound();
			}
		}

		public void OnLeaderUpdate(FlatPlayer newLeader) {
			leader = newLeader;
			foreach (FlatPlayer p in players) {
				rust.SendChatMessage(p.basePlayer, "", "<color=#02f0e8>" + newLeader.basePlayer.displayName + "</color> has been made leader.");
			}
		}
		#endregion

		#region Utils

		public FlatPlayer FindFlatPlayer(BasePlayer player) {
			foreach (FlatPlayer p in players) {
				if (p.basePlayer.Equals(player)) {
					return p;
				}
			}

			return null;
		}

		public List<FlatPlayer> GetTeamPlayers(FlatTeam team) {
			List<FlatPlayer> playerList = new List<FlatPlayer>();
			foreach (FlatPlayer p in players) {
				if (p.team == team) {
					playerList.Add(p);
				}
			}

			return playerList;
		}

		private string GetKillMessage(FlatPlayer receiver, FlatPlayer killer, FlatPlayer victim, string weaponName, int distance) {
			string red = "#f02702";
			string blue = "#0281f0";
			string orange = "#f09902";
			string killerColor = red;
			string victimColor = red;

			if (receiver.team == FlatTeam.Spectator) {
				killerColor = orange;
				victimColor = orange;

			} else {
				if (receiver.team == killer.team) {
					killerColor = blue;

				} else {
					killerColor = red;
				}

				if (receiver.team == victim.team) {
					victimColor = blue;

				} else {
					victimColor = red;
				}
			}

			return "<color=" + killerColor + ">" + killer.basePlayer.displayName + "</color>" + " -> <color=" + victimColor + ">" + victim.basePlayer.displayName + "</color> (" + weaponName + ") | " + distance + "m";
		}

		void HealAndGiveKit(FlatPlayer p) {
			p.basePlayer.inventory.Strip();
			Utils.FullHeal(p.basePlayer);

			GiveKit(p, kitAttire, kitWeapons, kitAmmo, kitMeds);
		}

		void GiveKit(FlatPlayer player, List<string> attire, List<string> weapons, List<string> ammo, Dictionary<string, int> meds) {
			player.basePlayer.inventory.Strip();

			foreach (string a in attire) {
				player.basePlayer.inventory.containerWear.AddItem(ItemManager.FindItemDefinition(a), 1);
			}
			foreach (string w in weapons) {
				Item weaponItem = ItemManager.CreateByName(w);
				var heldEnt = weaponItem.GetHeldEntity() as BaseProjectile;
				if (heldEnt != null) {
					heldEnt.primaryMagazine.contents = heldEnt.primaryMagazine.capacity;
				}
				player.basePlayer.inventory.GiveItem(weaponItem);
			}
			foreach (string a in ammo) {
				player.basePlayer.inventory.GiveItem(ItemManager.CreateByName(a, 1000));
			}
			foreach (string m in meds.Keys) {
				player.basePlayer.inventory.GiveItem(ItemManager.CreateByName(m, meds[m]));
			}
		}

		public Vector3 GetSpawn(FlatTeam team) {
			float x = float.Parse(Utils.flatConfig[flatID.ToString(), team.ToString().ToLower() + "SpawnX"].ToString());
			float y = float.Parse(Utils.flatConfig[flatID.ToString(), team.ToString().ToLower() + "SpawnY"].ToString());
			float z = float.Parse(Utils.flatConfig[flatID.ToString(), team.ToString().ToLower() + "SpawnZ"].ToString());

			return new Vector3(x, y, z);
		}

		public Vector3 GetSpecSpawn() {
			float x = float.Parse(Utils.flatConfig[flatID.ToString(), "spectatorSpawnX"].ToString());
			float y = float.Parse(Utils.flatConfig[flatID.ToString(), "spectatorSpawnY"].ToString());
			float z = float.Parse(Utils.flatConfig[flatID.ToString(), "spectatorSpawnZ"].ToString());

			return new Vector3(x, y, z);
		}
		#endregion
	}

	public enum FlatTeam {
		A,
		B,
		Spectator
	}

	public class Utils {
		public static Vector3 worldSpawn = new Vector3(-185f, 8f, -344f);
		public static DynamicConfigFile flatConfig = Interface.Oxide.DataFileSystem.GetDatafile("FlatConfig");

		/// <summary>
		/// Picks player up if wounded, stops bleeding, heals to full
		/// </summary>
		/// <param name="player">Player to heal</param>
		public static void FullHeal(BasePlayer player) {
			player.StopWounded();
			player.metabolism.bleeding.value = 0;
			player.metabolism.SendChangesToClient();
			player.Heal(100);
		}

		public static void initFlatConfig() {
			flatConfig["0", "aSpawnX"] = 0;
			flatConfig["0", "aSpawnY"] = 5.2;
			flatConfig["0", "aSpawnZ"] = -70;

			flatConfig["0", "bSpawnX"] = 0;
			flatConfig["0", "bSpawnY"] = 5.2;
			flatConfig["0", "bSpawnZ"] = 70;

			flatConfig["0", "spectatorSpawnX"] = 0;
			flatConfig["0", "spectatorSpawnY"] = 5.2;
			flatConfig["0", "spectatorSpawnZ"] = 0;
			flatConfig.Save();
		}
	}

	public class FlatPlayer {
		// Team is "Spectator" if only spectator (not on a team)
		public BasePlayer basePlayer;
		public Flat flat;
		public bool isLeader;
		public bool isSpectating;
		public FlatTeam team;

		public FlatPlayer(BasePlayer player, Flat flat, bool isLeader, bool isSpectating, FlatTeam team) {
			this.basePlayer = player;
			this.flat = flat;
			this.isLeader = isLeader;
			this.isSpectating = isSpectating;
			this.team = team;
		}
	}
}
