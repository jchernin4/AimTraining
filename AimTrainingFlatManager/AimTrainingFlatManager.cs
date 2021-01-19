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
			Util.initFlatConfig();
			flats = new List<Flat>();
		}

		// Moves players back to their team's spawn when they are too far away from it
		object OnPlayerTick(BasePlayer player, PlayerTick msg, bool wasPlayerStalled) {
			foreach (Flat flat in flats) {
				if (!flat.isStarted) {
					string side = null;

					if (flat.aTeamPlayers.Contains(player)) {
						side = "a";

					} else if (flat.bTeamPlayers.Contains(player)) {
						side = "b";
					}

					if (side != null) {
						Vector3 curPos = player.ServerPosition;
						// TODO: Test this: Vector3 diff = g.GetSpawn(side) - curPos; and compare this instead (check if Math.abs(diff.x) or Math.abs(diff.z) is greater than 9)
						if (Math.Abs(curPos.x - flat.GetSpawn(side).x) > 9 || Math.Abs(curPos.z - flat.GetSpawn(side).z) > 9) {
							player.Teleport(flat.GetSpawn(side));
						}

						break;
					}
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
			bool isAttackerInFlat = false;
			Flat attackerFlat = null;
			foreach (Flat flat in flats) {
				// TODO: Theres got to be a better way of doing this (new player design should make this easier)
				if (flat.aTeamPlayers.Contains(attacker)) {
					if (flat.aTeamPlayers.Contains(victim)) {
						sameTeam = true;
					}
					isAttackerInFlat = true;
					attackerFlat = flat;
					break;

				} else if (flat.bTeamPlayers.Contains(attacker)) {
					if (flat.bTeamPlayers.Contains(victim)) {
						sameTeam = true;
					}
					isAttackerInFlat = true;
					attackerFlat = flat;
					break;
				}
			}

			// TODO: Remove "|| sameTeam" or add a flag (!isFriendlyFireEnabled && sameTeam) to enable friendly fire
			if (isAttackerInFlat && (!attackerFlat.isStarted || sameTeam)) {
				info.damageTypes.ScaleAll(0);
			}
			return null;
		}

		/*
		 * If player is going to die, instead of killing them have them heal and teleport to spectator spawn
		 */
		object OnPlayerDeath(BasePlayer player, HitInfo info) {
			/* 
			 * Let the player die if they are disconnected
			 * TODO: Uncomment so it doesn't block death of disconnected players. Commented because bots aren't "connected" to the server so they will also be allowed to be fully killed
			 */
			/*if (!player.IsConnected) {
				return null;
			}*/

			BasePlayer killer = info.InitiatorPlayer;
			// Checking if the killer is in a game
			bool isKillerInFlat = false;
			Flat killerFlat = null;
			foreach (Flat flat in flats) {
				if (flat.aTeamPlayers.Contains(killer) || flat.bTeamPlayers.Contains(killer)) {
					isKillerInFlat = true;
					killerFlat = flat;
					break;
				}
			}

			if (isKillerInFlat) {
				killerFlat.OnPlayerKilled(player, info, rust);

			} else {
				// This shouldn't happen (the killer wasn't in a game)
				player.StopWounded();
				player.Heal(100);
				player.Teleport(Util.worldSpawn);
			}

			// Returning anything other than null overwrites default behavior (stops them from dying)
			return 0;
		}

		void OnPlayerDisconnected(BasePlayer player) {
			// Killing player -> OnPlayerDeath hook, OnPlayerDeath lets the player die -> Flat.OnPlayerLeave should remove player from flat/end round if necessary
			player.Kill();
			foreach (Flat flat in flats) {
				if (flat.aTeamPlayers.Contains(player) || flat.bTeamPlayers.Contains(player)) {
					flat.OnPlayerLeave(player);
				}
			}
		}

		// Spawn at spawn
		object OnPlayerRespawn(BasePlayer player) {
			return new BasePlayer.SpawnPoint() { pos = Util.worldSpawn, rot = new UnityEngine.Quaternion(0, 0, 0, 1) };
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
					if (flat.aTeamPlayers.Concat(flat.bTeamPlayers).ToList().Contains(player)) {
						rust.SendChatMessage(player, "", "You are already in a flat. Type /leave to leave.", player.UserIDString);
						return;
					}

					if (flat.flatID == int.Parse(args[0])) {
						flatExists = true;
						runningFlat = flat;
						break;
					}
				}

				if (flatExists) {
					if (runningFlat.aTeamPlayers.Count > runningFlat.bTeamPlayers.Count) {
						runningFlat.OnPlayerJoinTeam(player, "b");
					} else {
						runningFlat.OnPlayerJoinTeam(player, "a");
					}

					/*
					 * If the game object was created but there aren't any active players as leader, leadership is transferred to the connecting player.
					 * Chances are, this will happen when there is nobody in the arena
					 */
					if (!runningFlat.aTeamPlayers.Contains(runningFlat.leader) && !runningFlat.bTeamPlayers.Contains(runningFlat.leader)) {
						runningFlat.OnLeaderUpdate(player);
					}

				} else {
					runningFlat = new Flat(rust);
					runningFlat.flatID = int.Parse(args[0]);
					// Don't call OnLeaderUpdate here because its the first time the flat is being created
					runningFlat.leader = player;
					flats.Add(runningFlat);

					runningFlat.OnPlayerJoinTeam(player, "a");
				}

				rust.SendChatMessage(player, "", "Sending you to flat " + args[0]);
			}
		}

		[ChatCommand("leave")]
		void Leave(BasePlayer player, string command, string[] args) {
			foreach (Flat flat in flats) {
				if (flat.aTeamPlayers.Contains(player) || flat.bTeamPlayers.Contains(player)) {
					if (flat.isStarted) {
						rust.SendChatMessage(player, "", "You may not leave a flat that has already started.");

					} else {
						flat.OnPlayerLeave(player);
					}

					return;
				}
			}
			rust.SendChatMessage(player, "", "You are not in a flat.");
		}

		[ChatCommand("spawn")]
		void TeleportToSpawn(BasePlayer player, string command, string[] args) {
			player.Teleport(Util.worldSpawn);
		}

		[ChatCommand("start")]
		void StartFlat(BasePlayer player, string command, string[] args) {
			foreach (Flat flat in flats) {
				if (flat.aTeamPlayers.Concat(flat.bTeamPlayers).ToList().Contains(player)) {
					if (flat.leader.Equals(player)) {
						if (!flat.isStarted) {
							if (flat.aTeamPlayers.Count > 0 && flat.bTeamPlayers.Count > 0) {
								flat.StartRound();
								foreach (BasePlayer p in flat.aTeamPlayers.Concat(flat.bTeamPlayers)) {
									rust.SendChatMessage(p, "", "A round has started!");
								}

								break;

							} else {
								rust.SendChatMessage(player, "", "There are not enough players on this flat to start.");
							}

						} else {
							rust.SendChatMessage(player, "", "A round has already started.");
						}

					} else {
						rust.SendChatMessage(player, "", "You are not this flat's leader. Ask " + flat.leader.displayName + " to type /start");
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
		public BasePlayer leader;
		public List<BasePlayer> aTeamPlayers;
		public List<BasePlayer> bTeamPlayers;
		public Dictionary<BasePlayer, string> spectators;

		private Game.Rust.Libraries.Rust rust;

		public List<string> kitAttire;
		public List<string> kitWeapons;
		public List<string> kitAmmo;
		public Dictionary<string, int> kitMeds;

		public bool isStarted;

		public Flat(Game.Rust.Libraries.Rust rust) {
			aTeamPlayers = new List<BasePlayer>();
			bTeamPlayers = new List<BasePlayer>();
			spectators = new Dictionary<BasePlayer, string>();

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

		public void StartRound() {
			isStarted = true;

			foreach (BasePlayer p in aTeamPlayers.Concat(bTeamPlayers).ToList()) {
				HealAndGiveKit(p);
			}
			// Allow movement/placing walls/damage/etc. (Movement is controlled in OnPlayerTick)
		}

		public void EndRound() {
			string winner = "";
			if (aTeamPlayers.Count == 0) {
				winner = "B";

			} else if (bTeamPlayers.Count == 0) {
				winner = "A";
			}

			foreach (BasePlayer p in aTeamPlayers.Concat(bTeamPlayers.Concat(spectators.Keys))) {
				rust.SendChatMessage(p, "", "Team " + winner + " won the round!");
			}

			foreach (BasePlayer s in spectators.Keys.ToList()) {
				string side = spectators[s];
				// Keeping spectators.remove and teleport inside ifs so that people that aren't on a team stay as spectators
				if (side.Equals("a")) {
					aTeamPlayers.Add(s);
					spectators.Remove(s);

				} else if (side.Equals("b")) {
					bTeamPlayers.Add(s);
					spectators.Remove(s);
				}
			}

			// TODO: Loop over players in the same loop after redesign
			foreach (BasePlayer p in aTeamPlayers) {
				HealAndGiveKit(p);
				p.Teleport(GetSpawn("a"));
			}

			foreach (BasePlayer p in bTeamPlayers) {
				HealAndGiveKit(p);
				p.Teleport(GetSpawn("b"));
			}

			isStarted = false;
			// Destroy placed walls, stop movement out of spawn, stop allowing placing of walls, stop damage, etc.
		}

		#region Events
		public void OnPlayerKilled(BasePlayer victim, HitInfo info, Game.Rust.Libraries.Rust rust) {
			// Send kill message
			BasePlayer killer = info.InitiatorPlayer;

			string killerTeam = null;

			if (aTeamPlayers.Contains(killer)) {
				killerTeam = "a";
			} else if (bTeamPlayers.Contains(killer)) {
				killerTeam = "b";
			}

			string victimTeam = null;
			if (aTeamPlayers.Contains(victim)) {
				victimTeam = "a";
			} else if (bTeamPlayers.Contains(victim)) {
				victimTeam = "b";
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

			// Get kill message with colors relative to team A
			string killMessage = GetKillMessage("a", killerTeam, victimTeam, killer.displayName, victim.displayName, weaponName, distance);
			foreach (BasePlayer gamePlayer in aTeamPlayers) {
				rust.SendChatMessage(gamePlayer, "", killMessage, killer.UserIDString);
			}


			// Get kill message with colors relative to team B
			killMessage = GetKillMessage("b", killerTeam, victimTeam, killer.displayName, victim.displayName, weaponName, distance);
			foreach (BasePlayer gamePlayer in bTeamPlayers) {
				rust.SendChatMessage(gamePlayer, "", killMessage, killer.UserIDString);
			}

			// Get kill message with colors relative to spectators
			killMessage = GetKillMessageSpectator(killer.displayName, victim.displayName, weaponName, distance);
			foreach (BasePlayer spectator in spectators.Keys) {
				rust.SendChatMessage(spectator, "", killMessage, killer.UserIDString);
			}

			victim.StopWounded();
			victim.metabolism.bleeding.value = 0;
			victim.metabolism.SendChangesToClient();
			victim.Heal(100);

			// Add victim to spectators
			if (victimTeam.Equals("a")) {
				aTeamPlayers.Remove(victim);
				spectators.Add(victim, "a");

			} else if (victimTeam.Equals("b")) {
				bTeamPlayers.Remove(victim);
				spectators.Add(victim, "b");
			}
			// Teleport victim to spectator area
			victim.Teleport(GetSpecSpawn());

			// If the game is finished
			if (aTeamPlayers.Count == 0 || bTeamPlayers.Count == 0) {
				EndRound();
			}
		}

		public void OnPlayerJoinTeam(BasePlayer player, string side) {
			player.metabolism.bleeding.value = 0;
			player.metabolism.SendChangesToClient();
			player.Heal(100);

			// Check if they just want to join spectators and not A/B
			if (side.Equals("s")) {
				aTeamPlayers.Remove(player);
				bTeamPlayers.Remove(player);
				spectators[player] = "s";

				player.Teleport(GetSpecSpawn());
				return;
			}

			// If the game isn't started, add player to team, else put them in spectators with the side they want to join saved for next round
			if (!isStarted) {
				if (side.Equals("a")) {
					aTeamPlayers.Add(player);
					bTeamPlayers.Remove(player);

				} else if (side.Equals("b")) {
					bTeamPlayers.Add(player);
					aTeamPlayers.Remove(player);

				} else

					HealAndGiveKit(player);
				player.Teleport(GetSpawn(side));

			} else {
				spectators.Add(player, side);
				player.Teleport(GetSpecSpawn());
			}

			string message = "<color=#02f0e8>" + player.displayName + "</color> has joined team " + side.ToUpper();
			foreach (BasePlayer p in aTeamPlayers) {
				rust.SendChatMessage(p, "", message);
			}
			foreach (BasePlayer p in bTeamPlayers) {
				rust.SendChatMessage(p, "", message);
			}
			foreach (BasePlayer p in spectators.Keys) {
				rust.SendChatMessage(p, "", message);
			}
		}

		// TODO: If a player leaves, the game won't end, so you should make it as if they died
		public void OnPlayerLeave(BasePlayer leftPlayer) {
			aTeamPlayers.Remove(leftPlayer);
			bTeamPlayers.Remove(leftPlayer);
			spectators.Remove(leftPlayer);

			// Check if they are connected, since this is called when players leave the server too
			if (leftPlayer.IsConnected) {
				leftPlayer.metabolism.calories.value = 500;
				leftPlayer.metabolism.hydration.value = 250;
				leftPlayer.metabolism.SendChangesToClient();

				leftPlayer.inventory.Strip();
				leftPlayer.Heal(100);

				leftPlayer.EnsureDismounted();
				if (leftPlayer.HasParent()) {
					leftPlayer.SetParent(null, true, true);
				}

				leftPlayer.EndLooting();
				leftPlayer.StartSleeping();
				leftPlayer.RemoveFromTriggers();

				leftPlayer.Teleport(Util.worldSpawn);
			}

			if (leader.Equals(leftPlayer)) {
				List<BasePlayer> allPlayers = aTeamPlayers.Concat(bTeamPlayers).ToList();
				if (allPlayers.Count > 0) {
					int index = new System.Random().Next(allPlayers.Count);
					leader = allPlayers[index];
					OnLeaderUpdate(leader);
				}
			}

			if (aTeamPlayers.Count == 0 || bTeamPlayers.Count == 0) {
				EndRound();
			}
		}

		public void OnLeaderUpdate(BasePlayer newLeader) {
			leader = newLeader;
			List<BasePlayer> allPlayers = aTeamPlayers.Concat(bTeamPlayers).Concat(spectators.Keys).ToList();
			foreach (BasePlayer p in allPlayers) {
				rust.SendChatMessage(p, "", "<color=#02f0e8>" + p.displayName + "</color> has been made leader.");
			}
		}
		#endregion

		#region Utils

		private string GetKillMessageSpectator(string killerName, string victimName, string weaponName, int distance) {
			// #f09902 is orange
			return "<color=#f09902>" + killerName + "</color>" + " -> <color=#f09902>" + victimName + "</color> (" + weaponName + ") | " + distance + "m";
		}

		private string GetKillMessage(string receiverTeam, string killerTeam, string victimTeam, string killerName, string victimName, string weaponName, int distance) {
			string red = "#f02702";
			string blue = "#0281f0";
			string killerColor = red;
			string victimColor = red;

			if (receiverTeam.Equals(killerTeam)) {
				killerColor = blue;
			}
			if (receiverTeam.Equals(victimTeam)) {
				victimColor = blue;
			}

			return "<color=" + killerColor + ">" + killerName + "</color>" + " -> <color=" + victimColor + ">" + victimName + "</color> (" + weaponName + ") | " + distance + "m";
		}

		void HealAndGiveKit(BasePlayer p) {
			p.inventory.Strip();
			p.StopWounded();
			p.metabolism.bleeding.value = 0;
			p.metabolism.SendChangesToClient();
			p.Heal(100);

			GiveKit(p, kitAttire, kitWeapons, kitAmmo, kitMeds);
		}

		void GiveKit(BasePlayer player, List<string> attire, List<string> weapons, List<string> ammo, Dictionary<string, int> meds) {
			player.inventory.Strip();

			foreach (string a in attire) {
				player.inventory.containerWear.AddItem(ItemManager.FindItemDefinition(a), 1);
			}
			foreach (string w in weapons) {
				Item weaponItem = ItemManager.CreateByName(w);
				var heldEnt = weaponItem.GetHeldEntity() as BaseProjectile;
				if (heldEnt != null) {
					heldEnt.primaryMagazine.contents = heldEnt.primaryMagazine.capacity;
				}
				player.inventory.GiveItem(weaponItem);
			}
			foreach (string a in ammo) {
				player.inventory.GiveItem(ItemManager.CreateByName(a, 1000));
			}
			foreach (string m in meds.Keys) {
				player.inventory.GiveItem(ItemManager.CreateByName(m, meds[m]));
			}
		}

		public Vector3 GetSpawn(string side) {
			float x = float.Parse(Util.flatConfig[flatID.ToString(), side + "SpawnX"].ToString());
			float y = float.Parse(Util.flatConfig[flatID.ToString(), side + "SpawnY"].ToString());
			float z = float.Parse(Util.flatConfig[flatID.ToString(), side + "SpawnZ"].ToString());

			return new Vector3(x, y, z);
		}

		public Vector3 GetSpecSpawn() {
			float x = float.Parse(Util.flatConfig[flatID.ToString(), "spectatorSpawnX"].ToString());
			float y = float.Parse(Util.flatConfig[flatID.ToString(), "spectatorSpawnY"].ToString());
			float z = float.Parse(Util.flatConfig[flatID.ToString(), "spectatorSpawnZ"].ToString());

			return new Vector3(x, y, z);
		}
		#endregion
	}

	public class Util {
		public static Vector3 worldSpawn = new Vector3(-185f, 8f, -344f);
		public static DynamicConfigFile flatConfig = Interface.Oxide.DataFileSystem.GetDatafile("FlatConfig");
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
}
