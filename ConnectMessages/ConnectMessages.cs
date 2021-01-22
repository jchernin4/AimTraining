using Oxide.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oxide.Plugins {
	[Info("Connect Messages", "Fyre", "1.0.0")]
	public class ConnectMessages : RustPlugin {
		void OnPlayerConnected(BasePlayer player) {
			Server.Broadcast("<color=#00bbee>" + player.displayName + "</color> <color=#ffffff>has joined the server.</color>");
		}

		void OnPlayerDisconnected(BasePlayer player, string reason) {
			Server.Broadcast("<color=#00bbee>" + player.displayName + "</color> <color=#ffffff>has disconnected.");
		}
	}
}
