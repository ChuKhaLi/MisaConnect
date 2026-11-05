import http from 'node:http';
import https from 'node:https';
import { Socket } from 'node:net';
import { describe, expect, it } from 'vitest';
import {
  CONNECT_ATTEMPT_TIMEOUT_MS,
  createNodeHttpsFetch,
} from '../../src/infrastructure/node-https-fetch.js';

type Created = { protocol: string; options: https.AgentOptions };

/** Agent factory whose sockets fail with ECONNREFUSED without touching the network. */
function refusingFactory(created: Created[]) {
  return (protocol: 'http:' | 'https:', options: https.AgentOptions): http.Agent => {
    const agent = protocol === 'https:' ? new https.Agent(options) : new http.Agent(options);
    (agent as unknown as { createConnection: () => Socket }).createConnection = () => {
      const socket = new Socket();
      process.nextTick(() =>
        socket.destroy(Object.assign(new Error('connect ECONNREFUSED'), { code: 'ECONNREFUSED' })),
      );
      return socket;
    };
    created.push({ protocol, options });
    return agent;
  };
}

describe('node-https-fetch agent options', () => {
  it('allows 2 s per address attempt instead of the Node default of 250 ms', () => {
    expect(CONNECT_ATTEMPT_TIMEOUT_MS).toBe(2_000);
  });

  it.each(['https://misa.invalid/a', 'http://misa.invalid/a'])(
    'passes autoSelectFamilyAttemptTimeout to the %s agent',
    async (url) => {
      const created: Created[] = [];
      const f = createNodeHttpsFetch({ agentFactory: refusingFactory(created) });
      await f(url).catch(() => undefined);
      expect(created[0]?.options.autoSelectFamilyAttemptTimeout).toBe(2_000);
      f.close();
    },
  );
});
