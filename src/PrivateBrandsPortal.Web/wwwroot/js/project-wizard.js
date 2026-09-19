(() => {
    "use strict";
    // One shared client parser; server DecimalModelBinder remains authoritative.
    function decimalValue(value) {
        const text = value.trim();
        return /^[+-]?\d+(?:[.,]\d{1,2})?$/.test(text) ? Number(text.replace(",", ".")) : NaN;
    }
    if (window.jQuery && jQuery.validator) {
        jQuery.validator.methods.number = function(value, element) {
            return this.optional(element) || Number.isFinite(decimalValue(value));
        };
        jQuery.validator.methods.range = function(value, element, limits) {
            const number = decimalValue(value);
            return this.optional(element) || (number >= Number(limits[0]) && number <= Number(limits[1]));
        };
    }
    document.querySelectorAll("form[data-confirm]").forEach(form => {
        form.addEventListener("submit", event => {
            if (!window.confirm(form.dataset.confirm)) event.preventDefault();
        });
    });
})();

