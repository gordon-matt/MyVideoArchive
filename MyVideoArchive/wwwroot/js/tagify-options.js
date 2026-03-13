/**
 * Shared Tagify options so Enter adds the typed tag (instead of selecting first suggestion)
 * and space does not insert a space (tags are single-word; space can add current word as tag).
 * Use getTagifyOptions(whitelist, overrides) when creating Tagify instances.
 */

/**
 * @param {string[]} whitelist - Tag names for autocomplete
 * @param {object} overrides - Optional overrides (e.g. maxTags, dropdown)
 * @returns {object} Tagify options
 */
export function getTagifyOptions(whitelist = [], overrides = {}) {
    return {
        whitelist: whitelist || [],
        enforceWhitelist: false,
        maxTags: 20,
        pattern: /^\S+$/,  // no spaces in a single tag
        dropdown: {
            maxItems: 20,
            classname: 'tags-look',
            enabled: 1,
            closeOnSelect: false,
            highlightFirst: false   // so Enter adds typed value instead of selecting first suggestion
        },
        hooks: {
            keydown(e) {
                const tagify = e.detail?.tagify;
                const key = e.detail?.originalEvent?.key;
                if (!tagify) return;
                const val = (tagify.state.inputValue || '').trim();
                if (key === 'Enter') {
                    if (val) {
                        tagify.addTags(val);
                        tagify.state.inputValue = '';
                        e.detail.originalEvent.preventDefault();
                        return false;
                    }
                }
                if (key === ' ') {
                    e.detail.originalEvent.preventDefault();
                    if (val) {
                        tagify.addTags(val);
                        tagify.state.inputValue = '';
                    }
                    return false;
                }
            }
        },
        ...overrides,
        dropdown: {
            maxItems: 20,
            classname: 'tags-look',
            enabled: 1,
            closeOnSelect: false,
            highlightFirst: false,
            ...(overrides.dropdown || {})
        }
    };
}
