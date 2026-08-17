const BACKEND_URL = 'https://chess-arena-axvi.onrender.com';

export default {
  async fetch(request) {
    const url = new URL(request.url);
    url.protocol = 'https:';
    url.host = new URL(BACKEND_URL).host;

    const response = await fetch(url.toString(), {
      method: request.method,
      headers: request.headers,
      body: ['GET', 'HEAD'].includes(request.method) ? undefined : request.body,
      redirect: 'manual',
    });

    if (request.headers.get('upgrade')?.toLowerCase() === 'websocket' && response.webSocket) {
      return new Response(null, { status: 101, webSocket: response.webSocket });
    }

    return response;
  },
};