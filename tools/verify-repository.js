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

    // 과거 C01-C24/TASK-01..09 대신 현재 #16 문서와 실제 제품 구조의 일치를 확인한다.
    const spec = fs.readFileSync(path.join(root, 'docs', 'specs', 'SYNC_USB_MVP.md'), 'utf8');
    for (const term of ['StringBuilder', 'Encoding.Default.CodePage', 'FileShare.Read', 'CleanupCode', 'ConfirmPrintPosition', 'Bt003NativePlatform'])
        if (!spec.includes(term)) throw new Error(`Current MVP contract is missing ${term}.`);
    for (const file of ['README.md', 'AGENTS.md', 'docs/DESIGN.md', 'docs/IMPLEMENTATION_PLAN.md', 'docs/PRINTER_API.ko.md'])
        if (!fs.readFileSync(path.join(root, file), 'utf8').includes('SYNC_USB_MVP.md')) throw new Error(`Current contract link missing: ${file}`);
    const project = fs.readFileSync(path.join(root, 'src', 'U2BIO.BT003PROLabelPrinting', 'U2BIO.BT003PROLabelPrinting.csproj'), 'utf8');
    const core = [...project.matchAll(/<Compile Include="([^"]+)"/g)].map(m => m[1])
        .filter(file => !file.startsWith('Templates\\') && file !== 'Properties\\AssemblyInfo.cs' && file !== 'Sdk\\Interop\\BT003CtrlWrapper.cs');
    if (core.sort().join(',') !== 'Bt003Printer.cs,PrinterModels.cs') throw new Error('MVP must compile exactly two owned core sources.');
    process.stdout.write('Repository checks passed (documents and devflow structure; no product build or printer test).\n');
} catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
}
