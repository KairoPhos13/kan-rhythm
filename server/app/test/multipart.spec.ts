/**
 * Unit test for the game's multipart forms: osu!framework posts
 * POST /oauth/token and POST /users as multipart/form-data, and the fields
 * must reach the handlers exactly like a urlencoded body.
 */
import express from 'express';
import request from 'supertest';
import { multipartFields } from '../src/common/multipart';

function app() {
  const server = express();
  server.use(express.urlencoded({ extended: true }));
  server.use(multipartFields());
  server.post('/echo', (req, res) => res.json(req.body ?? {}));
  return server;
}

describe('multipart forms', () => {
  it('reads the token request fields the game sends', async () => {
    const response = await request(app())
      .post('/echo')
      .field('grant_type', 'password')
      .field('client_id', 'kan-client')
      .field('client_secret', 'kan-public-client')
      .field('username', 'KairoPhos')
      .field('password', 'a line from a song');
    expect(response.status).toBe(200);
    expect(response.body).toMatchObject({ grant_type: 'password', client_id: 'kan-client', username: 'KairoPhos' });
  });

  it('keeps the registration field names as the client writes them', async () => {
    const response = await request(app()).post('/echo').field('user[username]', 'KairoPhos');
    expect(response.status).toBe(200);
    expect(JSON.stringify(response.body)).toContain('KairoPhos');
  });

  it('refuses file uploads', async () => {
    const response = await request(app()).post('/echo').attach('file', Buffer.from('x'), 'x.txt');
    expect(response.status).toBe(400);
  });

  it('leaves urlencoded bodies alone', async () => {
    const response = await request(app()).post('/echo').type('form').send({ client_id: 'kan-client' });
    expect(response.body.client_id).toBe('kan-client');
  });
});
