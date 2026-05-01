$(document).ready(function () {
    drawCharts();
});

function drawCharts() {
    drawCandlestickChart('chart-1h', chartData.candles1H, 400, 300);
    drawCandlestickChart('chart-15m', chartData.candles15M, 400, 300);
    drawCandlestickChart('chart-5m', chartData.candles5M, 400, 300);
}

function drawCandlestickChart(canvasId, data, width, height) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) return;

    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');

    if (!data || data.length === 0) {
        ctx.fillStyle = '#8b949e';
        ctx.font = '14px monospace';
        ctx.fillText('No data available', 10, 50);
        return;
    }

    ctx.clearRect(0, 0, width, height);

    // Calculate price range with padding
    let minPrice = Math.min(...data.map(d => d.low));
    let maxPrice = Math.max(...data.map(d => d.high));
    let padding = (maxPrice - minPrice) * 0.1;
    minPrice -= padding;
    maxPrice += padding;
    let priceRange = maxPrice - minPrice;

    const candleWidth = Math.max(2, (width / data.length) - 2);

    for (let i = 0; i < data.length; i++) {
        const candle = data[i];
        const x = i * (candleWidth + 2);

        const highY = height - ((candle.high - minPrice) / priceRange) * height;
        const lowY = height - ((candle.low - minPrice) / priceRange) * height;
        const openY = height - ((candle.open - minPrice) / priceRange) * height;
        const closeY = height - ((candle.close - minPrice) / priceRange) * height;

        const isBullish = candle.close > candle.open;
        ctx.fillStyle = isBullish ? '#00ff88' : '#ff4444';
        ctx.strokeStyle = isBullish ? '#00ff88' : '#ff4444';

        // Draw wick
        ctx.beginPath();
        ctx.moveTo(x + candleWidth / 2, highY);
        ctx.lineTo(x + candleWidth / 2, lowY);
        ctx.stroke();

        // Draw body
        const bodyTop = Math.min(openY, closeY);
        const bodyBottom = Math.max(openY, closeY);
        const bodyHeight = Math.max(1, bodyBottom - bodyTop);

        ctx.fillRect(x, bodyTop, candleWidth, bodyHeight);
    }
}