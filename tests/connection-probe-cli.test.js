'use strict';
const assert = require('node:assert/strict');
const { parseArgs, safeReport, connectionPassed } = require('../tools/probe-native-load.js');
const base = ['--sdk-directory', 'synthetic-sdk'];
assert.deepEqual(parseArgs(base), { sdk: 'synthetic-sdk', connection: null });
for (const transport of ['usb', 'serial', 'tcp']) {
    const port = transport === 'usb' ? '0' : transport === 'serial' ? 'COM003' : '9100';
    const args = [...base, '--connection', transport, '--port', port, ...(transport === 'tcp' ? ['--ip', '192.0.2.1'] : [])];
    assert.deepEqual(parseArgs(args).connection, { transport, port, ip: transport === 'tcp' ? '192.0.2.1' : '' });
    const report = `CONNECTION|transport=${transport}|status=Passed|pre=0|setport=0|connect=0|open=1|disconnect=91|closed=0|printApiCalls=0|rfidApiCalls=0`;
    assert.ok(safeReport(report));
    assert.ok(connectionPassed(report, { transport }));
    assert.ok(!connectionPassed(report.replace('closed=0', 'closed=1'), { transport }));
    assert.ok(!connectionPassed(report, { transport: 'other' }));
    assert.ok(!safeReport(report + '|unreviewed=text'));
}
for (const args of [[], [...base, '--usb-connection'], [...base, '--connection', 'usb'],
    [...base, '--port', '0'], [...base, '--connection', 'usb', '--port', ''],
    [...base, '--connection', 'usb', '--port', '0\0x'], [...base, '--connection', 'usb', '--port', '0 '],
    [...base, '--connection', 'usb', '--port', '0', '--port', '1'],
    [...base, '--connection', 'usb', '--port', '0', '--ip', '192.0.2.1'],
    [...base, '--connection', 'serial', '--port', 'COM0'], [...base, '--connection', 'serial', '--port', 'COM-1'],
    [...base, '--connection', 'tcp', '--port', '65536', '--ip', '192.0.2.1'],
    [...base, '--connection', 'tcp', '--port', '9100', '--ip', 'example.invalid'],
    [...base, '--connection', 'tcp', '--port', '9100', '--ip', '192.0.2.1 ']])
    assert.throws(() => parseArgs(args), /ProbeUsage/);
assert.ok(safeReport('NATIVE_LOAD|status=Failed|code=InvalidConnectionOptions'));
console.log('PASS: explicit probe CLI options and report validation; no child or SDK execution');
