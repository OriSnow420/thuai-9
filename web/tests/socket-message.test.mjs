import assert from "node:assert/strict";
import {
  parseSocketMessageData,
  previewSocketPayload,
  readSocketPayload,
} from "../src/socket-message.js";

await testReadSocketPayloadSupportsString();
await testReadSocketPayloadSupportsArrayBuffer();
await testReadSocketPayloadSupportsTypedArrayView();
await testReadSocketPayloadSupportsBlob();
await testParseSocketMessageDataParsesJson();
testPreviewSocketPayloadCompactsWhitespace();

async function testReadSocketPayloadSupportsString() {
  assert.equal(await readSocketPayload('{"messageType":"PING"}'), '{"messageType":"PING"}');
}

async function testReadSocketPayloadSupportsArrayBuffer() {
  const buffer = new TextEncoder().encode('{"messageType":"PING"}').buffer;
  assert.equal(await readSocketPayload(buffer), '{"messageType":"PING"}');
}

async function testReadSocketPayloadSupportsTypedArrayView() {
  const bytes = new Uint8Array(new TextEncoder().encode('{"messageType":"PING"}'));
  assert.equal(await readSocketPayload(bytes), '{"messageType":"PING"}');
}

async function testReadSocketPayloadSupportsBlob() {
  const blob = new Blob(['{"messageType":"PING"}'], { type: "application/json" });
  assert.equal(await readSocketPayload(blob), '{"messageType":"PING"}');
}

async function testParseSocketMessageDataParsesJson() {
  const parsed = await parseSocketMessageData('{"messageType":"PING","value":1}');
  assert.equal(parsed.raw, '{"messageType":"PING","value":1}');
  assert.deepEqual(parsed.message, { messageType: "PING", value: 1 });
}

function testPreviewSocketPayloadCompactsWhitespace() {
  assert.equal(previewSocketPayload('  { \n  "messageType": "PING" \n }  '), '{ "messageType": "PING" }');
}
