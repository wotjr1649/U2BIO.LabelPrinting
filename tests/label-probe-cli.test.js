'use strict';
const assert = require('node:assert/strict');
const { parseArgs, safeReport, labelReturned } = require('../tools/probe-native-load.js');
const base = ['--sdk-directory', 'synthetic-sdk', '--connection', 'usb', '--port', '0'];
for (const architecture of ['x86', 'x64']) {
    const options = parseArgs([...base, '--label-test', '50x25gap3', '--architecture', architecture]);
    assert.equal(options.labelTest, '50x25gap3');
    assert.equal(options.architecture, architecture);
}
assert.equal(parseArgs([...base, '--label-test', 'blackmark60x20', '--architecture', 'x86']).labelTest, 'blackmark60x20');
for (const architecture of ['x86', 'x64'])
    assert.equal(parseArgs([...base, '--label-test', 'blackmark60x20ac', '--architecture', architecture]).labelTest, 'blackmark60x20ac');
assert.equal(parseArgs([...base, '--label-test', 'blackmark60x20direct', '--architecture', 'x86']).labelTest, 'blackmark60x20direct');
assert.ok(safeReport('DIRECT_LABEL|status=Returned|pre=0|setport=0|connect=0|open=1|disconnect=0|closed=0|send=0|invoked=1|completion=unknown|printApiCalls=0|rfidApiCalls=0'));
assert.ok(!safeReport('DIRECT_LABEL|status=Returned|pre=0|setport=0|connect=0|open=1|disconnect=0|closed=0|send=0|invoked=1|completion=confirmed|printApiCalls=0|rfidApiCalls=0'));
for (const extra of [ ['--label-test', '50x25gap3'], ['--architecture', 'x86'],
    ['--label-test', 'other', '--architecture', 'x86'], ['--label-test', '50x25gap3', '--architecture', 'both'] ])
    assert.throws(() => parseArgs([...base, ...extra]), /ProbeUsage/);
assert.throws(() => parseArgs(['--sdk-directory', 'synthetic-sdk', '--connection', 'serial', '--port', 'COM3', '--label-test', '50x25gap3', '--architecture', 'x86']), /ProbeUsage/);
const report = 'LABEL|status=Returned|pre=0|setport=0|connect=0|open=1|disconnect=0|closed=0|form=0|invoked=1|response=parsed|native=0|prefix=empty|length=2|rfidApiCalls=0';
assert.ok(safeReport(report));
assert.ok(labelReturned(report));
assert.ok(!labelReturned(report.replace('native=0', 'native=103')));
assert.ok(!labelReturned(report.replace('closed=0', 'closed=1')));
assert.ok(!safeReport(report.replace('prefix=empty', 'prefix=actual-id')));
assert.ok(!safeReport(report + '|raw=private'));
console.log('PASS: explicit one-architecture label diagnostic options and metadata-only reports; no child or SDK execution');
