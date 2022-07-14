using System;

namespace SteamKit2.SteamTicketAuth
{
    /// <summary>
    /// Represents a valid authorized session ticket.
    /// </summary>
    public class TicketInfo : IDisposable
    {
        internal uint AppID { get; }
        public uint CRC { get; }
        /// <summary>
        /// Bytes of the valid Session Ticket
        /// </summary>
        public byte[] Ticket { get; }
        public byte[] AuthToken { get; set; }

        public TicketInfo(uint appid, uint crc, byte[] ticket, byte[] token )
        {
            AppID = appid;
            CRC = crc;
            Ticket = ticket;
            AuthToken = token;
        }

        /// <summary>
        /// Tell steam we no longer use the ticket.
        /// </summary>
        public void Dispose()
        {
           
        }
      
    }
}
