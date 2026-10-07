(() => {
    const feedback = document.querySelector('[data-workflow-feedback]');
    if (!feedback) return;
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) { feedback.remove(); return; }
    if (feedback.dataset.workflowFeedback === 'delivered') {
        for (let i = 0; i < 24; i++) {
            const particle = document.createElement('span');
            particle.style.setProperty('--x', (i * 100 / 24) + 'vw');
            particle.style.setProperty('--delay', ((i % 5) * .07) + 's');
            feedback.append(particle);
        }
    }
    window.setTimeout(() => feedback.remove(), 2400);
})();
