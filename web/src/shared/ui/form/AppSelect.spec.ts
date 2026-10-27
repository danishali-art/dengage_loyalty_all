/** AppSelect keeps showing the chosen option's own label when its option list changes. */
import { afterEach, describe, expect, it } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { i18n } from '@/shared/i18n';
import AppSelect from './AppSelect.vue';

describe('AppSelect', () => {
  let wrapper: VueWrapper | undefined;
  afterEach(() => {
    wrapper?.unmount();
  });

  // Regression: items were keyed by index, so after the options changed the same index ('0')
  // named another option and the trigger kept the old label (a rule's type after a new trigger).
  it('shows the new label when the options and the value change together', async () => {
    wrapper = mount(AppSelect, {
      attachTo: document.body,
      global: { plugins: [i18n] },
      props: {
        id: 'type',
        modelValue: 'SpendRule',
        options: [
          { value: 'SpendRule', label: 'SpendRule' },
          { value: 'FixedBonusRule', label: 'FixedBonusRule' },
        ],
      },
    });
    await flushPromises();
    expect(wrapper.find('#type').text()).toBe('SpendRule');

    await wrapper.setProps({
      modelValue: 'RedemptionRule',
      options: [{ value: 'RedemptionRule', label: 'RedemptionRule' }],
    });
    await flushPromises();
    expect(wrapper.find('#type').text()).toBe('RedemptionRule');
  });

  it('accepts an empty-string value, which the primitives reject as an item value', async () => {
    wrapper = mount(AppSelect, {
      attachTo: document.body,
      global: { plugins: [i18n] },
      props: {
        id: 'period',
        modelValue: '',
        options: [
          { value: '', label: 'Choose…' },
          { value: 'Day', label: 'Day' },
        ],
      },
    });
    await flushPromises();
    expect(wrapper.find('#period').text()).toBe('Choose…');
  });

  // Regression: the search box opened pre-filled with the chosen item's key, so nothing matched.
  it('opens a searchable select with an empty search box, even with a value chosen', async () => {
    wrapper = mount(AppSelect, {
      attachTo: document.body,
      global: { plugins: [i18n] },
      props: {
        id: 'timezone',
        searchable: true,
        modelValue: 'UTC',
        options: [
          { value: 'UTC', label: 'UTC' },
          { value: 'Asia/Riyadh', label: 'Asia/Riyadh' },
        ],
      },
    });
    await flushPromises();
    await wrapper.find('#timezone').trigger('click');
    await flushPromises();
    const search = document.querySelector<HTMLInputElement>('input[role="combobox"]')!;
    expect(search).not.toBeNull();
    expect(search.value).toBe('');
  });
});
