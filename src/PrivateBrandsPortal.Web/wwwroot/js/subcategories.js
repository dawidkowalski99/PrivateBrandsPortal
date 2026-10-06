(() => {
    const data = document.getElementById('subcategory-options');
    if (!data) return;
    const options = JSON.parse(data.textContent);
    document.querySelectorAll('[data-subcategory]').forEach(select => {
        const category = document.getElementById(select.dataset.category);
        category?.addEventListener('change', () => {
            select.replaceChildren(new Option('Select product subcategory', ''));
            options.filter(x => String(x.CategoryId) === category.value)
                .forEach(x => select.add(new Option(x.Name, String(x.Id))));
            select.value = '';
            select.required = true;
        });
    });
})();
