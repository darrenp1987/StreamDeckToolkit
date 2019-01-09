﻿using Newtonsoft.Json;
using StreamDeckLib.Messages;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamDeckLib
{

	/// <summary>
	/// This class manages the connection to the StreamDeck hardware
	/// </summary>
	public sealed class ConnectionManager : IDisposable
	{

		private string _Port;
		private string _Uuid;
		private string _RegisterEvent;
		private readonly ClientWebSocket _Socket = new ClientWebSocket();

		private static readonly Dictionary<string, Action<IStreamDeckPlugin, (string action, string context, Messages.StreamDeckEventPayload.Payload payload, string device)>> _ActionDictionary = new Dictionary<string, Action<IStreamDeckPlugin, (string action, string context, StreamDeckEventPayload.Payload payload, string device)>>() {

			{ "keyDown", (plugin, args) => plugin.OnKeyDown(args.action, args.context, args.payload, args.device) },
			{ "keyUp", (plugin, args) => plugin.OnKeyUp(args.action, args.context, args.payload, args.device)},
			{ "willAppear", (plugin, args) => plugin.OnWillAppear(args.action, args.context, args.payload, args.device)},
			{ "willDisappear", (plugin, args) => plugin.OnWillDisappear(args.action, args.context, args.payload, args.device)}
		};


		private ConnectionManager() { }

		public Messages.Info Info { get; private set; }

		public static ConnectionManager Initialize(string port, string uuid, string registerEvent, Messages.Info info) {

			var manager = new ConnectionManager()
			{
				_Port = port,
				_Uuid = uuid,
				_RegisterEvent = registerEvent,
				Info = info
			};

			return manager;
		}	

		public ConnectionManager SetPlugin(IStreamDeckPlugin plugin) {

			this._Plugin = plugin;
			plugin.Manager = this;
			return this;

		}

		public ConnectionManager Start(CancellationToken token) {

			Task.Factory.StartNew(() => Run(token), TaskCreationOptions.LongRunning);

			return this;

		}

		private async Task Run(CancellationToken token) {

			await _Socket.ConnectAsync(new Uri($"ws://localhost:{_Port}"), token);

			await _Socket.SendAsync(GetPluginRegistrationBytes(), WebSocketMessageType.Text, true, CancellationToken.None);

			var keepRunning = true;

			while(!token.IsCancellationRequested)
			{

				// Exit loop if the socket is closed or aborted
				switch (_Socket.State)
				{

					case WebSocketState.CloseReceived:
					case WebSocketState.Closed:
					case WebSocketState.Aborted:
						keepRunning = false;
						break;

				}
				if (!keepRunning) break;

				var jsonString = await GetMessageAsString(token);

				if (!string.IsNullOrEmpty(jsonString))
				{

					var msg = JsonConvert.DeserializeObject<StreamDeckEventPayload>(jsonString);
					_ActionDictionary[msg.Event]?.Invoke(_Plugin, (msg.action, msg.context, msg.payload, msg.device));

				}

				await Task.Delay(100);

			}


		}

		public async Task SetTitleAsync(string context, string newTitle) {

			var args = new SetTitleArgs()
			{
				context = context,
				payload = new SetTitleArgs.Payload
				{
					title = newTitle,
					TargetType = SetTitleArgs.TargetType.HardwareAndSoftware
				}
			};

			var bytes = UTF8Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(args));
			await _Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

		}

		private async Task<string> GetMessageAsString(CancellationToken token)
		{
			byte[] buffer = new byte[65536];
			var segment = new ArraySegment<byte>(buffer, 0, buffer.Length);
			await _Socket.ReceiveAsync(segment, token);
			var jsonString = UTF8Encoding.UTF8.GetString(buffer);
			return jsonString;
		}

		private ArraySegment<byte> GetPluginRegistrationBytes()
		{
			var registration = new Messages.Info.PluginRegistration
			{
				@event = _RegisterEvent,
				uuid = _Uuid
			};

			var outString = JsonConvert.SerializeObject(registration);
			var outBytes = UTF8Encoding.UTF8.GetBytes(outString);

			return new ArraySegment<byte>(outBytes);
		}

		#region IDisposable Support
		private bool disposedValue = false; // To detect redundant calls
		private IStreamDeckPlugin _Plugin;

		void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					_Socket.Dispose();
				}

				disposedValue = true;
			}
		}

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			// TODO: uncomment the following line if the finalizer is overridden above.
			// GC.SuppressFinalize(this);
		}
		#endregion



	}

}
