'use strict';

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { spawnSync } = require('node:child_process');

if (process.platform === 'win32') process.env.NoDefaultCurrentDirectoryInExePath = '1';

const root = path.resolve(__dirname, '..');
const pluginRoot = process.env.DEVFLOW_ROOT || path.join(os.homedir(), '.codex', 'plugins', 'cache', 'wotjr1649', 'devflow', '0.3.0');
const doctor = path.join(pluginRoot, 'bin', 'devflow-doctor');

function run(command, args) {
    const result = spawnSync(command, args, {
        cwd: root, encoding: 'utf8', windowsHide: true, timeout: 30000, maxBuffer: 1024 * 1024
    });
    if (result.error) throw new Error(`Check process failed: ${result.error.code}`);
    if (result.status !== 0) throw new Error(`Check process exited ${result.status}: ${result.stdout || result.stderr}`);
    return result.stdout;
}

try {
    if (!fs.existsSync(doctor)) throw new Error('Installed devflow doctor not found. Set DEVFLOW_ROOT to its plugin root.');
    process.stdout.write(run(process.execPath, [doctor, root]));

    const baseline = fs.readFileSync(path.join(root, 'docs', 'BASELINE.md'), 'utf8');
    const entries = [...baseline.matchAll(/^\| (README\.md|docs\/[^ |]+\.md) \| `([a-f0-9]{64})` \|/gm)];
    if (entries.length !== 4) throw new Error('Expected four baseline document hashes.');
    for (const [, file, expected] of entries) {
        const snapshot = run('git', ['show', `v0.1.1-design:${file}`]).replace(/\r\n/g, '\n');
        const actual = crypto.createHash('sha256').update(snapshot, 'utf8').digest('hex');
        if (actual !== expected) throw new Error(`Baseline hash mismatch: ${file}`);
    }

    const design = fs.readFileSync(path.join(root, 'docs', 'DESIGN.md'), 'utf8');
    const cases = [...design.matchAll(/^\| (C\d{2}) \|/gm)].map(match => match[1]);
    const expectedCases = Array.from({ length: 24 }, (_, i) => `C${String(i + 1).padStart(2, '0')}`);
    if (cases.join(',') !== expectedCases.join(',')) throw new Error('C01-C24 acceptance cases are inconsistent.');
    const plan = fs.readFileSync(path.join(root, 'docs', 'IMPLEMENTATION_PLAN.md'), 'utf8');
    const tasks = [...plan.matchAll(/^## TASK-(\d{2}):/gm)].map(match => match[1]);
    const expectedTasks = Array.from({ length: 9 }, (_, i) => String(i + 1).padStart(2, '0'));
    if (tasks.join(',') !== expectedTasks.join(',')) throw new Error('Expected TASK-01 through TASK-09.');
    process.stdout.write('Repository checks passed (documents and devflow structure; no product build or printer test).\n');
} catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
}
