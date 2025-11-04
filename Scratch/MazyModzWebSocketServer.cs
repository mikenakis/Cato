namespace WebSocketTest;

// From https://github.com/MazyModz/CSharp-WebSocket-Server
// The code has obviously been ported from Java.
// The author is obviously a moron, since the original source code contained this line:
//     byte[] messageBuffer = new byte[8];
// and when someone pointed this out to him, instead of fixing it he responded with nonsense:
//     https://github.com/MazyModz/CSharp-WebSocket-Server/issues/3
// There is an alternative, but similar, example here:
//     https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API/Writing_WebSocket_server
//     but it is even more lame.(It contains busy spin-loops and other nonsense.)

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MikeNakis.Kit;
using MikeNakis.Kit.Extensions;

/// <summary>
/// Holds data for a encoded message frame
/// </summary>
readonly struct FrameMaskData
{
	public readonly int DataLength;
	public readonly int KeyIndex;
	public readonly int TotalLenght;
	public readonly OpcodeType Opcode;

	public FrameMaskData( int dataLength, int keyIndex, int totalLenght, OpcodeType opcode )
	{
		DataLength = dataLength;
		KeyIndex = keyIndex;
		TotalLenght = totalLenght;
		Opcode = opcode;
	}
}

/// <summary>
/// Enum for opcode types
/// </summary>
[Flags]
enum OpcodeType
{
#pragma warning disable CA1008 // Enums should have zero value
	Fragment = 0,
#pragma warning restore CA1008 // Enums should have zero value
	Text = 1,
	Binary = 2,
	ClosedConnection = 8,
	Ping = 9,
	Pong = 10
}

/// <summary>
/// Helper methods for the Server and Client class
/// </summary>
static class Helpers
{
	/// <summary>Gets data for a encoded websocket frame message</summary>
	/// <param name="data">The data to get the info from</param>
	/// <returns>The frame data</returns>
	public static FrameMaskData GetFrameMaskData( byte[] data )
	{
		// Get the opcode of the frame
		int opcode = data[0] - 128;

		// If the length of the message is in the 2 first indexes
		if( data[1] - 128 <= 125 )
		{
			int dataLength = data[1] - 128;
			return new FrameMaskData( dataLength, 2, dataLength + 6, (OpcodeType)opcode );
		}

		// If the length of the message is in the following two indexes
		if( data[1] - 128 == 126 )
		{
			// Combine the bytes to get the length
			int dataLength = BitConverter.ToInt16( new byte[] { data[3], data[2] }, 0 );
			return new FrameMaskData( dataLength, 4, dataLength + 8, (OpcodeType)opcode );
		}

		// If the data length is in the following 8 indexes
		if( data[1] - 128 == 127 )
		{
			// Get the following 8 bytes to combine to get the data 
			byte[] combine = new byte[8];
			for( int i = 0; i < 8; i++ )
				combine[i] = data[i + 2];

			// Combine the bytes to get the length
			//int dataLength = (int)BitConverter.ToInt64(new byte[] { Data[9], Data[8], Data[7], Data[6], Data[5], Data[4], Data[3], Data[2] }, 0);
			int dataLength = (int)BitConverter.ToInt64( combine, 0 );
			return new FrameMaskData( dataLength, 10, dataLength + 14, (OpcodeType)opcode );
		}

		// error
		throw new AssertionFailureException();
	}

	/// <summary>Gets the opcode of a frame</summary>
	/// <param name="frame">The frame to get the opcode from</param>
	/// <returns>The opcode of the frame</returns>
	public static OpcodeType GetFrameOpcode( byte[] frame )
	{
		return (OpcodeType)frame[0] - 128;
	}

	/// <summary>Gets the decoded frame data from the given byte array</summary>
	/// <param name="data">The byte array to decode</param>
	/// <returns>The decoded data</returns>
	public static string GetDataFromFrame( byte[] data )
	{
		FrameMaskData frameMaskData = GetFrameMaskData( data );

		// Get the decode frame key from the frame data
		byte[] decodeKey = new byte[4];
		for( int i = 0; i < 4; i++ )
			decodeKey[i] = data[frameMaskData.KeyIndex + i];

		int dataIndex = frameMaskData.KeyIndex + 4;
		int count = 0;

		// Decode the data using the key
		for( int i = dataIndex; i < frameMaskData.TotalLenght; i++ )
		{
			data[i] = (byte)(data[i] ^ decodeKey[count % 4]);
			count++;
		}

		// Return the decoded message 
		return Encoding.Default.GetString( data, dataIndex, frameMaskData.DataLength );
	}

	/// <summary>Checks if a byte array is valid</summary>
	/// <param name="buffer">The byte array to check</param>
	/// <returns>'true' if the byte array is valid</returns>
	public static bool GetIsBufferValid( ref byte[] buffer )
	{
		if( buffer == null )
			return false;
		if( buffer.Length <= 0 )
			return false;

		return true;
	}

	/// <summary>Gets an encoded websocket frame to send to a client from a string</summary>
	/// <param name="message">The message to encode into the frame</param>
	/// <param name="opcode">The opcode of the frame</param>
	/// <returns>Byte array in form of a websocket frame</returns>
	public static byte[] GetFrameFromString( string message, OpcodeType opcode = OpcodeType.Text )
	{
		byte[] response;
		byte[] bytesRaw = Encoding.Default.GetBytes( message );
		byte[] frame = new byte[10];

		int indexStartRawData;
		int length = bytesRaw.Length;

		frame[0] = (byte)(128 + (int)opcode);
		if( length <= 125 )
		{
			frame[1] = (byte)length;
			indexStartRawData = 2;
		}
		else if( length is >= 126 and <= 65535 )
		{
			frame[1] = 126;
			frame[2] = (byte)(length >> 8 & 255);
			frame[3] = (byte)(length & 255);
			indexStartRawData = 4;
		}
		else
		{
			frame[1] = 127;
			frame[2] = (byte)(length >> 56 & 255);
			frame[3] = (byte)(length >> 48 & 255);
			frame[4] = (byte)(length >> 40 & 255);
			frame[5] = (byte)(length >> 32 & 255);
			frame[6] = (byte)(length >> 24 & 255);
			frame[7] = (byte)(length >> 16 & 255);
			frame[8] = (byte)(length >> 8 & 255);
			frame[9] = (byte)(length & 255);

			indexStartRawData = 10;
		}

		response = new byte[indexStartRawData + length];

		int i, reponseIdx = 0;

		//Add the frame bytes to the reponse
		for( i = 0; i < indexStartRawData; i++ )
		{
			response[reponseIdx] = frame[i];
			reponseIdx++;
		}

		//Add the data bytes to the response
		for( i = 0; i < length; i++ )
		{
			response[reponseIdx] = bytesRaw[i];
			reponseIdx++;
		}

		return response;
	}

	/// <summary>Hash a request key with SHA1 to get the response key</summary>
	/// <param name="key">The request key</param>
	/// <returns></returns>
	public static string HashKey( string key )
	{
		const string handshakeKey = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
		string longKey = key + handshakeKey;

		byte[] hashBytes = SHA1.HashData( Encoding.ASCII.GetBytes( longKey ) );

		return Convert.ToBase64String( hashBytes );
	}

	/// <summary>Gets the http request string to send to the websocket client</summary>
	/// <param name="key">The SHA1 hashed key to respond with</param>
	/// <returns></returns>
	public static string GetHandshakeResponse( string key )
	{
		return $"HTTP/1.1 101 Switching Protocols\nUpgrade: WebSocket\nConnection: Upgrade\nSec-WebSocket-Accept: {key}\r\n\r\n";
	}

	/// <summary>Gets the WebSocket handshake updgrade key from the http request</summary>
	/// <param name="httpRequest">The http request string to get the key from</param>
	/// <returns></returns>
	public static string GetHandshakeRequestKey( string httpRequest )
	{
		int keyStart = httpRequest.IndexOf2( "Sec-WebSocket-Key: " ) + 19;
		string key = "";
		for( int i = keyStart; i < keyStart + 24; i++ )
			key += httpRequest[i];
		return key;
	}

	/// <summary>Creates a random guid with a prefix</summary>
	/// <param name="prefix">The prefix of the id; null = no prefix</param>
	/// <param name="length">The length of the id to generate</param>
	/// <returns>The random guid. Ex. Prefix-XXXXXXXXXXXXXXXX</returns>
	public static string CreateGuid( string prefix, int length = 16 )
	{
		string final = "";
		string ids = "0123456789abcdefghijklmnopqrstuvwxyz";

		var random = new Random();

		// Loop and get a random index in the ids and append to id 
		for( short i = 0; i < length; i++ )
			final += ids[random.Next( 0, ids.Length )];

		// Return the guid without a prefix
		if( prefix == null )
			return final;

		// Return the guid with a prefix
		return $"{prefix}-{final}";
	}
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

///<summary>
/// Object for all connectecd clients
/// </summary>
public partial class Client
{
	///<summary>The socket of the connected client</summary>
	readonly Socket socket;

	///<summary>The guid of the connected client</summary>
	readonly string guid;

	/// <summary>The server that the client is connected to</summary>
	readonly Server server;

	/// <summary>If the server has sent a ping to the client and is waiting for a pong</summary>
	bool isWaitingForPong;

	/// <summary>Create a new object for a connected client</summary>
	/// <param name="server">The server object instance that the client is connected to</param>
	/// <param name="socket">The socket of the connected client</param>
	public Client( Server server, Socket socket )
	{
		this.server = server;
		this.socket = socket;
		guid = Helpers.CreateGuid( "client" );

		// Start to detect incomming messages 
		GetSocket().BeginReceive( new byte[] { 0 }, 0, 0, SocketFlags.None, messageCallback, null );
	}

	/// <summary>Gets the guid of the connected client</summary>
	/// <returns>The GUID of the client</returns>
	public string GetGuid()
	{
		return guid;
	}

	///<summary>Gets the socket of the connected client</summary>
	///<returns>The socket of the client</returns>
	public Socket GetSocket()
	{
		return socket;
	}

	/// <summary>The socket that this client is connected to</summary>
	/// <returns>Listen socket</returns>
	public Server GetServer()
	{
		return server;
	}

	/// <summary>Gets if the server is waiting for a pong response</summary>
	/// <returns>If the server is waiting for a pong response</returns>
	public bool GetIsWaitingForPong()
	{
		return isWaitingForPong;
	}

	/// <summary>Sets if the server is waiting for a pong response</summary>
	/// <param name="isWaitingForPong">If the server is waiting for a pong response</param>
	public void SetIsWaitingForPong( bool isWaitingForPong )
	{
		this.isWaitingForPong = isWaitingForPong;
	}

	/// <summary>Called when a message was received from the client</summary>
	void messageCallback( IAsyncResult asyncResult )
	{
		//try
		//{
		GetSocket().EndReceive( asyncResult );

		// Read the incomming message 
		byte[] messageBuffer = new byte[1024];
		int bytesReceived = GetSocket().Receive( messageBuffer );

		// Resize the byte array to remove whitespaces 
		if( bytesReceived < messageBuffer.Length )
			Array.Resize( ref messageBuffer, bytesReceived );

		// Get the opcode of the frame
		OpcodeType opcode = Helpers.GetFrameOpcode( messageBuffer );

		// If the connection was closed
		if( opcode == OpcodeType.ClosedConnection )
		{
			GetServer().ClientDisconnect( this );
			return;
		}

		// Pass the message to the server event to handle the logic
		GetServer().ReceiveMessage( this, Helpers.GetDataFromFrame( messageBuffer ) );

		// Start to receive messages again
		GetSocket().BeginReceive( new byte[] { 0 }, 0, 0, SocketFlags.None, messageCallback, null );
		//}
		//catch( Exception exception )
		//{
		//	Log.Warn( "", exception );
		//	GetSocket().Close();
		//	GetSocket().Dispose();
		//	GetServer().ClientDisconnect( this );
		//}
	}
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

/// <summary>
/// Handler for when a message was received
/// </summary>
public class OnMessageReceivedHandler : EventArgs
{
	/// <summary>The client that send the message</summary>
	readonly Client client;

	/// <summary>The message the client sent</summary>
	readonly string message;

	/// <summary>Create a new message received event handler</summary>
	/// <param name="client">The client that sent the message</param>
	/// <param name="message">The message the client sent</param>
	public OnMessageReceivedHandler( Client client, string message )
	{
		this.client = client;
		this.message = message;
	}

	/// <summary>Get the client that sent the received message</summary>
	/// <returns>The client that sent the message</returns>
	public Client GetClient()
	{
		return client;
	}

	/// <summary>The message that was received from the client</summary>
	/// <returns>The received message</returns>
	public string GetMessage()
	{
		return message;
	}
}

/// <summary>
/// Handler for when a message was send to a client
/// </summary>
public class OnSendMessageHandler : EventArgs
{
	/// <summary>The client the message was sent to</summary>
	readonly Client client;

	/// <summary>The message that was sent to the client</summary>
	readonly string message;

	/// <summary>Create a new handler for when a message was sent</summary>
	/// <param name="client">The client the message was sent to</param>
	/// <param name="message">The message that was sent to the client</param>
	public OnSendMessageHandler( Client client, string message )
	{
		this.client = client;
		this.message = message;
	}

	/// <summary>The client the message was sent to</summary>
	/// <returns>The client receiver</returns>
	public Client GetClient()
	{
		return client;
	}

	/// <summary>The message that was send to the client</summary>
	/// <returns>The sent message</returns>
	public string GetMessage()
	{
		return message;
	}
}

/// <summary>
/// Handler for when a client connected
/// </summary>
public class OnClientConnectedHandler : EventArgs
{
	/// <summary>The client that connected to the server</summary>
	readonly Client client;

	/// <summary>Create a new event handler for when a client connected</summary>
	/// <param name="client">The client that connected</param>
	public OnClientConnectedHandler( Client client )
	{
		this.client = client;
	}

	/// <summary>Get the client that was connected</summary>
	/// <returns>The client that connected </returns>
	public Client GetClient()
	{
		return client;
	}
}

/// <summary>
/// Handler for when a client disconnects
/// </summary>
public class OnClientDisconnectedHandler : EventArgs
{
	/// <summary>The client that diconnected</summary>
	readonly Client client;

	/// <summary>Create a new handler for when a client disconnects</summary>
	/// <param name="client">The disconnected client</param>
	public OnClientDisconnectedHandler( Client client )
	{
		this.client = client;
	}

	/// <summary>Gets the client that disconnected</summary>
	/// <returns>The disconnected client</returns>
	public Client GetClient()
	{
		return client;
	}
}

///<summary>
/// Object for all listen servers
///</summary>
public class Server : IDisposable
{
	/// <summary>Called after a message was sent</summary>
	public event EventHandler<OnSendMessageHandler>? OnSendMessage;

	/// <summary>Called when a client was connected to the server (after handshake)</summary>
	public event EventHandler<OnClientConnectedHandler>? OnClientConnected;

	/// <summary>Called when a message was received from a connected client</summary>
	public event EventHandler<OnMessageReceivedHandler>? OnMessageReceived;

	/// <summary>Called when a client disconnected</summary>
	public event EventHandler<OnClientDisconnectedHandler>? OnClientDisconnected;

	/// <summary>The listen socket (server socket)</summary>
	readonly Socket socket;

	/// <summary>The listen ip end point of the server</summary>
	readonly IPEndPoint endPoint;

	/// <summary>The connected clients to the server </summary>
	readonly List<Client> clients = new List<Client>();

	/// <summary>Create and start a new listen socket server</summary>
	/// <param name="endPoint">The listen endpoint of the server</param>
	public Server( IPEndPoint endPoint )
	{
		this.endPoint = endPoint;

		// Create a new listen socket
		socket = new Socket( AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp );

		Console.WriteLine( "Copyright © 2017 - MazyModz. Created by Dennis Andersson. All rights reserved.\n\n" );
		Console.WriteLine( "WebSocket Server Started\nListening on {0}:{1}\n", GetEndPoint().Address.ToString(), GetEndPoint().Port );

		// Start the server
		start();
	}

	public void Dispose()
	{
		socket.Dispose();
	}

	/// <summary>Gets the listen socket</summary>
	/// <returns>The listen socket</returns>
	public Socket GetSocket()
	{
		return socket;
	}

	/// <summary>Get the listen socket endpoint</summary>
	/// <returns>The listen socket endpoint</returns>
	public IPEndPoint GetEndPoint()
	{
		return endPoint;
	}

	/// <summary>Gets a connected client at the given index</summary>
	/// <param name="index">The connected client array index</param>
	/// <returns>The connected client at the index, returns null if the index is out of bounds</returns>
	public Client? GetConnectedClient( int index )
	{
		if( index < 0 || index >= clients.Count )
			return null;
		return clients[index];
	}

	/// <summary>Gets a connected client with the given guid</summary>
	/// <param name="guid">The Guid of the client to get</param>
	/// <returns>The client with the given id, return null if no client with the guid could be found</returns>
	public Client? GetConnectedClient( string guid )
	{
		foreach( Client client in clients )
			if( client.GetGuid() == guid )
				return client;
		return null;
	}

	/// <summary>Gets a connected client with the given socket</summary>
	/// <param name="socket">The socket of the client </param>
	/// <returns>The connected client with the given socket, returns null if no client with the socket was found</returns>
	public Client? GetConnectedClient( Socket socket )
	{
		foreach( Client client in clients )
			if( client.GetSocket() == socket )
				return client;
		return null;
	}

	/// <summary>Get the number of clients that are connected to the server</summary>
	/// <returns>The number of connected clients</returns>
	public int GetConnectedClientCount()
	{
		return clients.Count;
	}

	/// <summary>
	/// Starts the listen server when a server object is created
	/// </summary>
	void start()
	{
		// Bind the socket and start listending
		GetSocket().Bind( GetEndPoint() );
		GetSocket().Listen( 0 );

		// Start to accept clients and accept incomming connections 
		GetSocket().BeginAccept( connectionCallback, null );
	}

	/// <summary>
	/// Stops the listen server 
	/// </summary>
	public void Stop()
	{
		GetSocket().Close();
		GetSocket().Dispose();
	}

	/// <summary>Called when the socket is trying to accept an incomming connection</summary>
	/// <param name="asyncResult">The async operation state</param>
	void connectionCallback( IAsyncResult asyncResult )
	{
		//try
		//{
		// Gets the client thats trying to connect to the server
		Socket clientSocket = GetSocket().EndAccept( asyncResult );

		// Read the handshake updgrade request
		byte[] handshakeBuffer = new byte[1024];
		int handshakeReceived = clientSocket.Receive( handshakeBuffer );

		// Get the hanshake request key and get the hanshake response
		string requestKey = Helpers.GetHandshakeRequestKey( Encoding.Default.GetString( handshakeBuffer ) );
		string hanshakeResponse = Helpers.GetHandshakeResponse( Helpers.HashKey( requestKey ) );

		// Send the handshake updgrade response to the connecting client 
		clientSocket.Send( Encoding.Default.GetBytes( hanshakeResponse ) );

		// Create a new client object and add 
		// it to the list of connected clients
		var client = new Client( this, clientSocket );
		clients.Add( client );

		// Call the event when a client has connected to the listen server 
		if( OnClientConnected == null )
			throw new Exception( "Server error: event OnClientConnected is not bound!" );
		OnClientConnected( this, new OnClientConnectedHandler( client ) );

		// Start to accept incomming connections again 
		GetSocket().BeginAccept( connectionCallback, null );
		//}
		//catch( Exception exception )
		//{
		//	Console.WriteLine( "An error has occured while trying to accept a connecting client.\n\n{0}", exception.Message );
		//}
	}

	/// <summary>Called when a message was recived, calls the OnMessageReceived event</summary>
	/// <param name="client">The client that sent the message</param>
	/// <param name="message">The message that the client sent</param>
	public void ReceiveMessage( Client client, string message )
	{
		if( OnMessageReceived == null )
			throw new Exception( "Server error: event OnMessageReceived is not bound!" );
		OnMessageReceived( this, new OnMessageReceivedHandler( client, message ) );
	}

	/// <summary>Called when a client disconnectes, calls event OnClientDisconnected</summary>
	/// <param name="client">The client that disconnected</param>
	public void ClientDisconnect( Client client )
	{
		// Remove the client from the connected clients list
		clients.Remove( client );

		// Call the OnClientDisconnected event
		if( OnClientDisconnected == null )
			throw new Exception( "Server error: OnClientDisconnected is not bound!" );
		OnClientDisconnected( this, new OnClientDisconnectedHandler( client ) );
	}

	/// <summary>Send a message to a connected client</summary>
	/// <param name="client">The client to send the data to</param>
	/// <param name="data">The data to send the client</param>
	public void SendMessage( Client client, string data )
	{
		// Create a websocket frame around the data to send
		byte[] frameMessage = Helpers.GetFrameFromString( data );

		// Send the framed message to the in client
		client.GetSocket().Send( frameMessage );

		// Call the on send message callback event 
		if( OnSendMessage == null )
			throw new Exception( "Server error: event OnSendMessage is not bound!" );
		OnSendMessage( this, new OnSendMessageHandler( client, data ) );
	}
}
