using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using SteamKit2;
using SteamKit2.Internal;
using SteamKit2.Util;


namespace SteamKit2.SteamTicketAuth

{
    /// <summary>
    /// 
    /// </summary>
    public class SteamAuthTicketHandler : ClientMsgHandler
    {
        private static Random _rnd = new Random();
        private TaskCompletionSource<byte[]> _ownerTicketTcs;

        private readonly Dictionary<EMsg, Action<IPacketMsg>> _dispatchMap;
        private readonly ConcurrentQueue<byte[]> _gameConnectTokens = new ConcurrentQueue<byte[]>();
        private DateTime _serverTime;
        private readonly uint _steamPipe = ( uint )_rnd.Next( 100000, 999999 );
        private readonly ConcurrentDictionary<uint, List<CMsgAuthTicket>> _ticketsByGame = new ConcurrentDictionary<uint, List<CMsgAuthTicket>>();

        private readonly ConcurrentDictionary<ulong, CMsgAuthTicket> tickets = new ConcurrentDictionary<ulong, CMsgAuthTicket>();
        private uint seq = 1;
        private uint server_seq = 1;

        internal SteamAuthTicketHandler()
        {
            _dispatchMap = new Dictionary<EMsg, Action<IPacketMsg>>
            {
                {EMsg.ClientAuthListAck, HandleTicketAcknowledged},
                {EMsg.ClientTicketAuthComplete, HandleTicketAuthComplete},
                {EMsg.ClientGameConnectTokens, HandleGameConnectTokens},
                {EMsg.ClientLogOnResponse, HandleLogOnResponse},
                {EMsg.ClientGetAppOwnershipTicketResponse, HandleAppOwnershipTicketResponse },
            };

        }
        /// <summary>
        /// Handles a client message. This should not be called directly.
        /// </summary>
        /// <param name="packetMsg">The packet message that contains the data.</param>
        public override void HandleMsg( IPacketMsg packetMsg )
        {
            if ( packetMsg == null )
            {
                throw new ArgumentNullException( nameof( packetMsg ) );
            }

            if ( _dispatchMap.TryGetValue( packetMsg.MsgType, out var handlerFunc ) )
            {
                handlerFunc( packetMsg );
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="appid"></param>
        /// <returns></returns>
        public async Task<TicketInfo?> GetAuthSessionTicketExt( uint appid )
        {
            if ( !_gameConnectTokens.TryDequeue( out var token ) ) return null;


            var authToken = CreateAuthToken( token );
            _ = VerifyTicket( appid, authToken, out var crc );
            var res = await VerifyTicket( appid, authToken, out  crc );
            return res.ActiveTicketsCRC.Any( x => x == crc ) ?
                new TicketInfo( appid, crc, null, authToken ) :
                null;
        }

        public void SkipFirstClientTicket( uint appid )
        {
            var ticket = CreateTicket( appid );
            SendTicket(appid, ticket );
        }


        public TicketInfo CreateTicket( uint appid )
        {
            _gameConnectTokens.TryDequeue( out var token );

            var authToken = CreateAuthToken( token );
            var crc = Crc32.Compute( authToken );
            return new TicketInfo( 730, crc, null, authToken );

        }




        /// <summary>
        /// Generate session ticket, and verify it with steam servers.
        /// </summary>
        /// <param name="appid">The appid to request the ticket of.</param>
        /// <returns><c>null</c> if user isn't fully logged in, doesn't own the game, or steam deemed ticket invalid; otherwise <see cref="TicketInfo" /> instance.</returns>
        public async Task<TicketInfo> GetAuthSessionTicket( uint appid )

        {
            // not logged in
            if ( Client!.CellID == null )
            {
                return null;
            }

            /*var appTicket = await GetAppOwnershipTicket( appid );
            if ( appTicket == null )
            {
                return null;
            }*/

            if ( !_gameConnectTokens.TryDequeue( out var token ) ) return null;

            var authToken = CreateAuthToken( token );
            var ticketTask = await VerifyTicket( appid, authToken, out var crc );
            // verify the ticket is on the list of accepted tickets
            // didn't happen on my testing, but I don't think it hurts to check
            return ticketTask.ActiveTicketsCRC.Any( x => x == crc ) ?
                new TicketInfo( appid, crc, null, authToken ) :
                null;
        }

        /*public Task<ActionType> WaitAuthComplete()
        {
            _authCompleteTcs = new TaskCompletionSource<ActionType>();
            return _authCompleteTcs.Task;
        }*/


        public void VerifyTicket( TicketInfo ticket, ulong steamId )
        {
            var auth = new ClientMsgProtobuf<CMsgClientAuthList>( EMsg.ClientAuthList ) { Body = { tokens_left = 0 } };

            auth.Body.app_ids.Add( ticket.AppID );
            auth.Body.message_sequence = server_seq++;

            var newToken = new CMsgAuthTicket
            {
                estate = 1,
                eresult = 4294967295,
                gameid = ticket.AppID,
                ticket = ticket.AuthToken,
                ticket_crc = ticket.CRC,
                steamid = steamId,
                h_steam_pipe = _steamPipe /*131073*/

            };
            tickets[ steamId ] = newToken;
            auth.Body.tickets.AddRange( tickets.Values.Where( t => t != null ).ToList() );
           // auth.Body.tickets.Add( newToken );
         
            Client!.Send( auth );
            newToken.estate = 3;
        }



        private Task<byte[]> GetAppOwnershipTicket( uint appid )
        {
            var request = new ClientMsgProtobuf<CMsgClientGetAppOwnershipTicket>( EMsg.ClientGetAppOwnershipTicket )
            {
                Body = { app_id = appid }
            };


            Client!.Send( request );
            _ownerTicketTcs = new TaskCompletionSource<byte[]>();
            return _ownerTicketTcs.Task;
        }
        public bool CancelAuthTicket( TicketInfo ticket )
        {
            if ( !_ticketsByGame.TryGetValue( ticket.AppID, out var values ) ) return false;

            if ( values.RemoveAll( x => x.ticket_crc == ticket.CRC ) > 0 )
            {
                SendTickets();
            }

            return false;
        }

        

        private byte[] CreateAuthToken( byte[] gameConnectToken )
        {
            const int sessionSize =
                4 + // unknown 1
                4 + // unknown 2
                4 + // external IP
                4 + // padding
                4 + // connection time
                4; // connection count

            // We checked that we're connected before calling this function
            var ipAddress = NetHelpers.GetIPAddressAsUInt( Client!.PublicIP! );
            var connectionTime = ( int )( ( DateTime.UtcNow - _serverTime ).TotalMilliseconds );
            using var stream = new MemoryStream( 4 + gameConnectToken.Length + 4 + sessionSize );
            using ( var writer = new BinaryWriter( stream ) )
            {
                writer.Write( gameConnectToken.Length );
                writer.Write( gameConnectToken.ToArray() );

                writer.Write( sessionSize );
                writer.Write( 1 );
                writer.Write( 2 );

                writer.Write( ipAddress );
                writer.Write( 0 ); // padding
                writer.Write( connectionTime ); // in milliseconds
                writer.Write( 1 ); // single client connected
            }

            return stream.ToArray();
        }

        private AsyncJob<SteamAuthTicket.TicketAcceptedCallback> VerifyTicket( uint appid, byte[] authToken,
            out uint crc )
        {
            crc = Crc32.Compute( authToken );
            var items = _ticketsByGame.GetOrAdd( appid, new List<CMsgAuthTicket>() );
            items.Clear();
            // add ticket to specified games list
            items.Add( new CMsgAuthTicket
            {
                gameid = appid,
                ticket = authToken,
                ticket_crc = crc,
                h_steam_pipe = _steamPipe,
                eresult = 4294967295,
            } );
            return SendTickets();
        }


        private AsyncJob<SteamAuthTicket.TicketAcceptedCallback> SendTickets()
        {
            var auth = new ClientMsgProtobuf<CMsgClientAuthList>( EMsg.ClientAuthList )
            {
                Body = { tokens_left = ( uint )_gameConnectTokens.Count }
            };
            // all registered games
            auth.Body.app_ids.AddRange( _ticketsByGame.Keys );
            // flatten all registered per-game tickets
            auth.Body.tickets.AddRange( _ticketsByGame.Values.SelectMany( x => x ) );
            auth.Body.message_sequence = seq++;
            auth.SourceJobID = Client!.GetNextJobID();

            Client.Send( auth );
            return new AsyncJob<SteamAuthTicket.TicketAcceptedCallback>( Client, auth.SourceJobID );
        }

        public void SendTicket( uint appid, TicketInfo ticket )
        {
            var auth = new ClientMsgProtobuf<CMsgClientAuthList>( EMsg.ClientAuthList )
            {
                Body = { tokens_left = ( uint )_gameConnectTokens.Count }
            };
            // all registered games
            auth.Body.app_ids.Add( appid );
            // flatten all registered per-game tickets
            auth.Body.tickets.Add( new CMsgAuthTicket
            {
                gameid = appid,
                ticket = ticket.AuthToken,
                ticket_crc = ticket.CRC,
                h_steam_pipe = _steamPipe,
                eresult = 4294967295,
            } );
            auth.Body.message_sequence = seq++;
            auth.SourceJobID = Client!.GetNextJobID();
            Client.Send( auth );
        }



        #region ClientMsg Handlers

        void HandleAppOwnershipTicketResponse( IPacketMsg packetMsg )
        {
            var ticketResponse = new ClientMsgProtobuf<CMsgClientGetAppOwnershipTicketResponse>( packetMsg );

            var token = ticketResponse.Body.ticket;
            if ( ticketResponse.Body.eresult == 1 )
            {
                _ownerTicketTcs.SetResult( token );
            }
            else
            {
                _ownerTicketTcs.SetCanceled();
            }

        }

        private void HandleLogOnResponse( IPacketMsg packetMsg )
        {
            var body = new ClientMsgProtobuf<CMsgClientLogonResponse>( packetMsg ).Body;
            // just grabbing server time
            _serverTime = DateUtils.DateTimeFromUnixTime( body.rtime32_server_time );
        }

        private void HandleGameConnectTokens( IPacketMsg packetMsg )
        {
            var body = new ClientMsgProtobuf<CMsgClientGameConnectTokens>( packetMsg ).Body;

            // add tokens
            foreach ( var tok in body.tokens )
            {
                _gameConnectTokens.Enqueue( tok );
            }

            // keep only required amount, discard old entries
            while ( _gameConnectTokens.Count > body.max_tokens_to_keep )
            {
                _gameConnectTokens.TryDequeue( out _ );
            }
        }

        private void HandleTicketAuthComplete( IPacketMsg packetMsg )
        {

            var authComplete = new ClientMsgProtobuf<CMsgClientTicketAuthComplete>( packetMsg );
            var callback = new SteamAuthTicket.TicketAuthCompleteCallback( authComplete.TargetJobID, authComplete.Body );
            Client.PostCallback( callback );
        }

        private void HandleTicketAcknowledged( IPacketMsg packetMsg )
        {
            // ticket acknowledged as valid by steam
            var authAck = new ClientMsgProtobuf<CMsgClientAuthListAck>( packetMsg );
            var acknowledged = new SteamAuthTicket.TicketAcceptedCallback( authAck.TargetJobID, authAck.Body );
            Client.PostCallback( acknowledged );
        }

        #endregion


        public void RemoveTicket( ulong steamId )
        {
            if ( tickets.ContainsKey( steamId ) )
            {
                tickets.TryRemove( steamId, out _ );
            }
        }
    }
}
