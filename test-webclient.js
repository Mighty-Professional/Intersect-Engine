#!/usr/bin/env node
const puppeteer = require('/tmp/puppet/node_modules/puppeteer');

(async () => {
    let browser;
    try {
        browser = await puppeteer.launch({
            headless: false,
            args: ['--no-sandbox', '--disable-setuid-sandbox', '--enable-webgl', '--ignore-gpu-blocklist'],
            defaultViewport: { width: 1280, height: 720 },
        });

        const page = await browser.newPage();
        const logs = [];
        page.on('console', msg => logs.push(msg.text()));

        await page.goto('http://localhost:5174', { waitUntil: 'networkidle2', timeout: 30000 });
        console.log('Waiting 15s...');
        await new Promise(r => setTimeout(r, 15000));

        // Comprehensive button scan
        // Scan the entire lower half of the canvas in a grid
        console.log('Scanning lower half for clickable elements...');
        for (let y = 350; y <= 650; y += 15) {
            for (let x = 300; x <= 900; x += 30) {
                const before = logs.length;
                await page.mouse.click(x, y);
                await new Promise(r => setTimeout(r, 50));

                const newLogs = logs.slice(before).filter(l =>
                    l.includes('WebSocket') || l.includes('connect') ||
                    l.includes('Switch') || l.includes('clicked') ||
                    l.includes('Settings') || l.includes('Credits'));
                if (newLogs.length > 0) {
                    console.log(`FOUND at (${x}, ${y}): ${newLogs[0].substring(0, 100)}`);
                    await page.screenshot({ path: '/tmp/wc-found.png' });
                    await new Promise(r => setTimeout(r, 3000));

                    // Show all new logs after finding something
                    const allNew = logs.slice(before);
                    allNew.forEach(l => console.log('  ', l.substring(0, 200)));
                    break;
                }
            }
            // Check if we found something
            if (logs.some(l => l.includes('WebSocket connecting') || l.includes('Settings'))) break;
        }

        await page.screenshot({ path: '/tmp/wc-scan.png' });

        const wsLogs = logs.filter(l => l.includes('WebSocket'));
        console.log(`\nWebSocket logs: ${wsLogs.length}`);
        wsLogs.forEach(l => console.log('  ', l.substring(0, 200)));

        const frameErrors = logs.filter(l => l.includes('Frame error'));
        console.log(`Frame errors: ${frameErrors.length}`);
        frameErrors.forEach(l => console.log('  ', l.substring(0, 200)));

        console.log(`\nTotal logs: ${logs.length}`);

    } catch (err) {
        console.error('Failed:', err.message);
    } finally {
        if (browser) await browser.close();
    }
})();
