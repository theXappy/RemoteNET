﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ScubaDiver.API.Protocol;
using ScubaDiver.API.Utils;

public static class RnetProtocolParser
{
    private const int MagicValueLength = 4;
    private const int PayloadLengthFieldLength = 4;

    public static OverTheWireRequest? Parse(TcpClient tcpClient, CancellationToken token = default)
    {
        try
        {
            if (token == default)
                token = CancellationToken.None;

            OverTheWireRequest request = null;
            var stream = tcpClient.GetStream();
            while (tcpClient.Connected)
            {
                var magicValueBytes = new byte[MagicValueLength];
                if (!TryReadBytesFromStream(stream, magicValueBytes, token))
                    return null;

                if (!CheckMagicValue(magicValueBytes))
                {
                    // The magic value doesn't match, this is an invalid message
                    throw new Exception("Magic Mismatch");
                }

                var payloadLengthBytes = new byte[PayloadLengthFieldLength];
                if(!TryReadBytesFromStream(stream, payloadLengthBytes, token))
                    return null;

                var payloadLength = BitConverter.ToInt32(payloadLengthBytes, 0);
                if (payloadLength <= 0)
                {
                    // The payload length is invalid, this is an invalid message
                    continue;
                }

                var payloadBytes = new byte[payloadLength];
                if(!TryReadBytesFromStream(stream, payloadBytes, token))
                    return null;

                // Handle the payload bytes here...
                request = JsonConvert.DeserializeObject<OverTheWireRequest>(Encoding.UTF8.GetString(payloadBytes));
                break;
            }

            return request;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[@@@][{DateTime.Now}][RnetProtocolParser] Parse had an exception!. Exception: {ex}");
            throw;
        }
    }

    private static bool CheckMagicValue(byte[] bytes)
    {
        return bytes[0] == 'r' &&
               bytes[1] == 'N' &&
               bytes[2] == 'E' &&
               bytes[3] == 'T';
    }

    private static bool TryReadBytesFromStream(Stream stream, byte[] buffer, CancellationToken token)
    {
        var bytesRead = 0;
        var bytesToRead = buffer.Length;

        while (bytesToRead > 0)
        {
            var n =  stream.ReadAsync(buffer, bytesRead, bytesToRead, token).Result;
            if (n == 0)
            {
                // The connection was closed by the remote endpoint, terminate 
                return false;
            }

            bytesRead += n;
            bytesToRead -= n;
        }

        return true;
    }

    private static byte[] Encode(string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var messageLength = MagicValueLength + PayloadLengthFieldLength + bodyBytes.Length;
        var messageBytes = new byte[messageLength];

        // Write the magic value bytes
        var magicValueBytes = Encoding.ASCII.GetBytes("rNET");
        Array.Copy(magicValueBytes, 0, messageBytes, 0, MagicValueLength);

        // Write the payload length bytes
        var payloadLengthBytes = BitConverter.GetBytes(bodyBytes.Length);
        Array.Copy(payloadLengthBytes, 0, messageBytes, MagicValueLength, PayloadLengthFieldLength);

        // Write the payload bytes
        Array.Copy(bodyBytes, 0, messageBytes, MagicValueLength + PayloadLengthFieldLength, bodyBytes.Length);

        return messageBytes;
    }

    public static void Write(TcpClient client, OverTheWireRequest resp, CancellationToken token = default)
    {
        if (token == default)
            token = CancellationToken.None;

        var respJson = JsonConvert.SerializeObject(resp);
        byte[] encdoded = RnetProtocolParser.Encode(respJson);

        client.GetStream().WriteAsync(encdoded, 0, encdoded.Length, token).Wait(token);
    }
}