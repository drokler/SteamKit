using System;
using System.IO;
using ProtoBuf.Meta;
using SteamKit2.GC;
using SteamKit2.GC.CSGO.Internal;
using SteamKit2.Internal;

namespace SteamKit2
{
    public partial class SteamGameCoordinator
    {
        /// <summary>
        /// This callback is fired when a game coordinator message is recieved from the network.
        /// </summary>
        public class MessageCallback : CallbackMsg
        {
            // raw emsg (with protobuf flag, if present)
            uint eMsg;

            /// <summary>
            /// Gets the game coordinator message type.
            /// </summary>
            public uint EMsg { get { return MsgUtil.GetGCMsg( eMsg ); } }
            /// <summary>
            /// Gets the AppID of the game coordinator the message is from.
            /// </summary>
            public uint AppID { get; private set; }
            /// <summary>
            /// Gets a value indicating whether this message is protobuf'd.
            /// </summary>
            /// <value>
            ///   <c>true</c> if this instance is protobuf'd; otherwise, <c>false</c>.
            /// </value>
            public bool IsProto { get { return MsgUtil.IsProtoBuf( eMsg ); } }

            /// <summary>
            /// Gets the actual message.
            /// </summary>
            public IPacketGCMsg Message { get; private set; }


            internal MessageCallback( CMsgGCClient gcMsg )
            {
                this.eMsg = gcMsg.msgtype;
                this.AppID = gcMsg.appid;
                this.Message = GetPacketGCMsg( gcMsg.msgtype, gcMsg.payload );
                this.JobID = this.Message.TargetJobID;
            }


            static IPacketGCMsg GetPacketGCMsg( uint eMsg, byte[] data )
            {
                // strip off the protobuf flag
                uint realEMsg = MsgUtil.GetGCMsg( eMsg );

                if ( MsgUtil.IsProtoBuf( eMsg ) )
                {
                    return new PacketClientGCMsgProtobuf( realEMsg, data );
                }
                else
                {
                    return new PacketClientGCMsg( realEMsg, data );
                }
            }
        }



        /// <summary>
        /// This callback is fired when a game coordinator message is recieved from the network.
        /// </summary>
        public class ClientWelcomeMessageCallback : MessageCallback
        {
            internal ClientWelcomeMessageCallback(CMsgGCClient gcMsg) : base(gcMsg)
            {
            }
        }

        /// <summary>
        /// This callback is fired when a game coordinator message is recieved from the network.
        /// </summary>
        public class ServerWelcomeMessageCallback : MessageCallback
        {
            internal ServerWelcomeMessageCallback( CMsgGCClient gcMsg ) : base( gcMsg )
            {
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class ClientRequestJoinServerDataMessageCallback : MessageCallback
        {
            internal ClientRequestJoinServerDataMessageCallback( CMsgGCClient gcMsg ) : base( gcMsg )
            {
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        public class UpdateMultipleMessageCallback : MessageCallback
        {
            internal UpdateMultipleMessageCallback( CMsgGCClient gcMsg ) : base( gcMsg )
            {
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class ServerDataResponseMessageCallback : MessageCallback
        {
            internal ServerDataResponseMessageCallback( CMsgGCClient gcMsg ) : base( gcMsg )
            {
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class ClientConnectionStatusMessageCallback : MessageCallback
        {
            /// <summary>
            /// NoSession
            /// </summary>
            public bool NoSession { get; }
            internal ClientConnectionStatusMessageCallback( CMsgGCClient gcMsg ) : base( gcMsg )
            {
                var msg = new ClientGCMsgProtobuf<CMsgConnectionStatus>( Message );
                NoSession = msg.Body.status == GCConnectionStatus.GCConnectionStatus_NO_SESSION;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class OnItemCreateMessageCallback : MessageCallback
        {
            /// <summary>
            /// 
            /// </summary>
            public CSOEconItem Item { get; }
            internal OnItemCreateMessageCallback( CMsgGCClient gcMsg ) : base( gcMsg )
            {
                var msg = new ClientGCMsgProtobuf<CMsgSOSingleObject>( Message );
                using var ms = new MemoryStream( msg.Body.object_data );
                Item = ( CSOEconItem )RuntimeTypeModel.Default.Deserialize( ms, null, typeof( CSOEconItem ) );

            }
        }

    }
}
