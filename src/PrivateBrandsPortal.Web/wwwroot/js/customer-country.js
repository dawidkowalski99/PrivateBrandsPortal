// Apply suggestions only on an explicit customer change: Back/refresh/Edit retain the project country.
document.querySelectorAll('[data-country-target]').forEach(customer => {
    const country = document.getElementById(customer.dataset.countryTarget);
    if (!country) return;
    customer.addEventListener('change', () => {
        const suggested = customer.selectedOptions[0]?.dataset.defaultCountry || '';
        country.value = Array.from(country.options).some(option => option.value === suggested) ? suggested : '';
        country.dispatchEvent(new Event('change', { bubbles: true }));
    });
});
