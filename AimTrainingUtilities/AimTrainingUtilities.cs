using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oxide.Plugins {
	[Info("Aim Training Utilities", "Fyre", "0.0.1")]
	public class AimTrainingUtilities : RustPlugin {
		#region Oxide Hooks
		void Init() {
			Server.Command("decay.upkeep", "false");
			Server.Command("hotairballoon.population", "0");

			Puts("Setting time to 12, clearing weather");
			Server.Command("env.time", "12");
			Server.Command("weather.load", "clear");

			timer.Every(300, () => {
				Puts("Setting time to 12, clearing weather");
				Server.Command("env.time", "12");
				Server.Command("weather.load", "clear");
			});

			timer.Every(1200, () => {
				foreach (BasePlayer p in BasePlayer.allPlayerList.ToList()) {
					if (p.IsSleeping()) {
						p.Kill();
					}
				}
			});
		}

		void OnPlayerConnected(BasePlayer player) {
			Server.Broadcast("<color=#00bbee>" + player.displayName + "</color> <color=#ffffff>has joined.</color>");
		}

		void OnPlayerDisconnected(BasePlayer player, string reason) {
			Server.Broadcast("<color=#00bbee>" + player.displayName + "</color> <color=#ffffff>has disconnected.");
		}

		void OnItemDropped(Item item, BaseEntity entity) {
			// Prevent dropping to teammates / spectators (Prevent giving spectators guns like ukn)
			entity.Kill();
		}

		void OnPlayerCorpseSpawned(BasePlayer player, BaseCorpse corpse) {
			// Kill body if it spawns
			corpse.Kill();
		}

		object OnRunPlayerMetabolism(PlayerMetabolism metabolism, BasePlayer player, float delta) {
			metabolism.calories.value = 500;
			metabolism.hydration.value = 250;
			// TODO: Could probably just return 0 instead of changing values, this will keep their calories/hydration the same. May affect health though, you might want to send just the health
			return null;
		}

		object OnPlayerRespawned(BasePlayer player) {
			player.metabolism.calories.value = 500;
			player.metabolism.hydration.value = 250;
			player.metabolism.SendChangesToClient();

			player.inventory.Strip();
			player.Heal(100);

			return null;
		}
		#endregion



		#region Commands
		///////////////////
		/// For Testing ///
		///////////////////
		[ChatCommand("clear")]
		void ClearInventory(BasePlayer player) {
			player.inventory.Strip();
			player.BroadcastMessage("Cleared your inventory");
		}
		#endregion
	}
}
