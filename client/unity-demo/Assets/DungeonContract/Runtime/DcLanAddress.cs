using System;
using System.IO;
using System.Net;

namespace DungeonContract
{
    public static class DcLanAddress
    {
        public static string Validate(string value)
        {
            if(!IPAddress.TryParse(value,out var address)||address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||address.ToString()!=value)throw new InvalidDataException("Use a plain IPv4 address");
            var bytes=address.GetAddressBytes();
            if(!(bytes[0]==10||bytes[0]==127||bytes[0]==26||bytes[0]==192&&bytes[1]==168||bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31))throw new InvalidDataException("Only explicit local, LAN or Radmin IPv4 addresses are accepted");
            return value;
        }
        public static string PublicURL(string host)=>"http://"+Validate(host)+":12880";
        public static string WebSocketURL(string host)=>"ws://"+Validate(host)+":12880/ws";
        public static void ValidateAdmission(string host,string endpoint)
        {
            if(endpoint!=Validate(host)+":12777")throw new InvalidDataException("Admission battle endpoint does not match the selected server");
        }
    }
}
