using System;
using System.Collections.Generic;
using SteamKit2.GC;
using SteamKit2.GC.CSGO.Internal;
using SteamKit2.Internal;

namespace SteamKit2
{
    /// <summary>
    /// This handler handles all game coordinator messaging.
    /// </summary>
    public sealed partial class SteamGameCoordinator : ClientMsgHandler
    {
        Dictionary<EMsg, Action<IPacketMsg>> dispatchMap;

        internal SteamGameCoordinator()
        {
            dispatchMap = new Dictionary<EMsg, Action<IPacketMsg>>
            {
                { EMsg.ClientFromGC, HandleFromGC },

            };
        }


        /// <summary>
        /// Sends a game coordinator message for a specific appid.
        /// </summary>
        /// <param name="msg">The GC message to send.</param>
        /// <param name="appId">The app id of the game coordinator to send to.</param>
        public void Send( IClientGCMsg msg, uint appId )
        {
            var clientMsg = BuildGameCoordinatorMessage(msg, appId);

            this.Client.Send( clientMsg );
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="appId"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static ClientMsgProtobuf<CMsgGCClient> BuildGameCoordinatorMessage(IClientGCMsg msg, uint appId)
        {
            if (msg == null)
            {
                throw new ArgumentNullException(nameof(msg));
            }

            var clientMsg = new ClientMsgProtobuf<CMsgGCClient>(EMsg.ClientToGC);

            clientMsg.ProtoHeader.routing_appid = appId;
            clientMsg.Body.msgtype = MsgUtil.MakeGCMsg(msg.MsgType, msg.IsProto);
            clientMsg.Body.appid = appId;

            clientMsg.Body.payload = msg.Serialize();
            return clientMsg;
        }


        /// <summary>
        /// Handles a client message. This should not be called directly.
        /// </summary>
        /// <param name="packetMsg">The packet message that contains the data.</param>
        public override void HandleMsg( IPacketMsg packetMsg )
        {
            if ( packetMsg == null )
            {
                throw new ArgumentNullException( nameof(packetMsg) );
            }

            if ( !dispatchMap.TryGetValue( packetMsg.MsgType, out var handlerFunc ) )
            {
                // ignore messages that we don't have a handler function for
                return;
            }

            handlerFunc( packetMsg );
        }


        #region ClientMsg Handlers
        void HandleFromGC( IPacketMsg packetMsg )
        {
            var msg = new ClientMsgProtobuf<CMsgGCClient>( packetMsg );

            var callback = new MessageCallback( msg.Body );

            switch ( callback.Message.MsgType )
            {
                case (uint)EGCBaseClientMsg.k_EMsgGCClientWelcome:
                    this.Client.PostCallback( new ClientWelcomeMessageCallback(msg.Body));
                    break;

                case ( uint )ESOMsg.k_ESOMsg_Create:
                    this.Client.PostCallback( new OnItemCreateMessageCallback( msg.Body ) );
                    break;

                case ( uint )ECsgoGCMsg.k_EMsgGCCStrike15_v2_ClientRequestJoinServerData:
                    this.Client.PostCallback( new ClientRequestJoinServerDataMessageCallback( msg.Body ) );
                    break;

                case ( uint )EGCBaseClientMsg.k_EMsgGCClientConnectionStatus:
                    this.Client.PostCallback( new ClientConnectionStatusMessageCallback( msg.Body ) );
                    break;

                case ( uint )ESOMsg.k_ESOMsg_UpdateMultiple:
                    this.Client.PostCallback( new UpdateMultipleMessageCallback( msg.Body ) );
                    break;
            }


            this.Client.PostCallback( callback );
        }
        #endregion
    }
}
