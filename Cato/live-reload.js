const websocket = new WebSocket( "ws://localhost:8080/live-reload-websocket" );
websocket.onopen = e => log( "WebSocket connected" );
websocket.onclose = e => log( "WebSocket closed" );
websocket.onerror = e => log( "WebSocket error! All we know is: '" + e.type + "'" );

websocket.onmessage = e =>
{
	log( "WebSocket received " + e.data );
	this.window.document.location.reload();
};

function log( text ) { console.log( "live-reload.js: " + text ); }
