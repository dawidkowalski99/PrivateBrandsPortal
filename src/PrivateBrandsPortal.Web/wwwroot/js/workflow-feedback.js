(() => {
    const root = document.querySelector('[data-workflow-feedback]');
    if (!root) return;
    const event = root.dataset.workflowFeedback;
    if (!['SalesAndDeliveryCompleted', 'CustomerRejected'].includes(event)) { root.remove(); return; }
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    if (reduced.matches) { root.remove(); return; }
    let started = false;
    const start = () => {
        if (started || document.hidden) return;
        started = true;
        document.removeEventListener('visibilitychange', start);
        // Keep the overlay outside page cards/containers and start only when the page is visible.
        document.body.append(root);
        if (event === 'CustomerRejected') {
            root.classList.add('workflow-rejected');
            const badge = document.createElement('span');
            badge.className = 'workflow-rejected-icon';
            badge.textContent = '×';
            root.append(badge);
            window.setTimeout(() => root.remove(), 2200);
            return;
        }
        const canvas = document.createElement('canvas');
        root.append(canvas);
        const width = window.innerWidth, height = window.innerHeight;
        const scale = Math.min(window.devicePixelRatio || 1, 2);
        canvas.width = width * scale; canvas.height = height * scale;
        const ctx = canvas.getContext('2d');
        if (!ctx) { root.remove(); return; }
        ctx.scale(scale, scale);
        const colors = ['#168d89', '#2775d3', '#f0ba3c', '#d85078', '#624fc2'];
        const particles = Array.from({length: 110}, (_, i) => ({
            x: width * ((i * 0.61803398875) % 1), y: -30 - (i % 9) * 18,
            vx: Math.sin(i * 2.4) * 90, vy: 130 + (i % 7) * 25,
            rotation: i, spin: (i % 2 ? 1 : -1) * 3,
            color: colors[i % colors.length], size: 7 + (i % 4) * 2
        }));
        let previous, beginning;
        const frame = now => {
            if (reduced.matches) { root.remove(); return; }
            beginning ??= now; previous ??= now;
            const elapsed = now - beginning, dt = Math.min((now - previous) / 1000, .04);
            previous = now;
            ctx.clearRect(0, 0, width, height);
            ctx.globalAlpha = Math.min(1, Math.max(0, (2200 - elapsed) / 550));
            for (const p of particles) {
                p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 170 * dt; p.rotation += p.spin * dt;
                ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.rotation);
                ctx.fillStyle = p.color; ctx.fillRect(-p.size / 2, -p.size / 3, p.size, p.size * .65); ctx.restore();
            }
            if (elapsed < 2200) requestAnimationFrame(frame); else root.remove();
        };
        requestAnimationFrame(frame);
    };
    const ready = () => {
        document.addEventListener('visibilitychange', start);
        requestAnimationFrame(() => requestAnimationFrame(start));
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready, {once:true});
    else ready();
})();
