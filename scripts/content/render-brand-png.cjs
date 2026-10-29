// Renders the brand PNGs from their SVG sources (the logo, the logo's mark alone, and the social-sharing image) with
// a headless Chromium, so a colour change in the SVGs reaches the PNGs that Open Graph and JSON-LD point to.
//
//   node scripts/content/render-brand-png.cjs
//
// PLAYWRIGHT_MODULE and BROWSER_PATH as for scripts/baseline/capture.cjs. Transparent background except where the SVG
// paints one (the OG image has its own white rectangle).

const fs = require('fs');
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');

const assets = path.join(__dirname, '..', '..', 'NovaGloben.Website', 'Server', 'wwwroot', 'assets');
const jobs = [
    { svg: 'logo-full.svg', png: 'logo-full.png', width: 1093, height: 200 },
    // The mark alone is the logo's first 174 x 200 units (the hexagon).
    { svg: 'logo-full.svg', png: 'logo-icon.png', width: 174, height: 200, viewBox: '0 0 174 200' },
    { svg: 'og-image.svg', png: 'og-image.png', width: 1200, height: 630 },
];

(async () =>
{
    const browser = await chromium.launch(process.env.BROWSER_PATH ? { executablePath: process.env.BROWSER_PATH } : {});
    for (const job of jobs)
    {
        let svg = fs.readFileSync(path.join(assets, job.svg), 'utf8');
        svg = svg.replace(/<svg[^>]*>/, m => m
            .replace(/\swidth="[^"]*"/, ` width="${job.width}"`)
            .replace(/\sheight="[^"]*"/, ` height="${job.height}"`)
            .replace(/\sviewBox="[^"]*"/, job.viewBox ? ` viewBox="${job.viewBox}"` : '$&'));
        const page = await browser.newPage({ viewport: { width: job.width, height: job.height } });
        await page.setContent(`<html><body style="margin:0;background:transparent">${svg}</body></html>`);
        await page.locator('svg').screenshot({ path: path.join(assets, job.png), omitBackground: true });
        await page.close();
        console.log(`${job.png} ${job.width}x${job.height} from ${job.svg}`);
    }
    await browser.close();
})();
