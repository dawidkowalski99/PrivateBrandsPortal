(() => {
    const reason = document.getElementById('rejection-reason');
    const comment = document.getElementById('review-comment');
    if (!reason || !comment) return;
    const update = () => { comment.required = reason.selectedOptions[0]?.dataset.comment === 'true'; };
    reason.addEventListener('change', update);
    update();
})();
