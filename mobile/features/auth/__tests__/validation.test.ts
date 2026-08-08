import {
  consumerRegisterSchema,
  staffLoginSchema,
} from '../validation';

function messages(result: ReturnType<typeof staffLoginSchema.safeParse>): string[] {
  return result.success ? [] : result.error.issues.map((issue) => issue.message);
}

describe('mobile auth validation localization', () => {
  it('uses field-specific Ukrainian messages for an empty staff login', () => {
    expect(messages(staffLoginSchema.safeParse({ email: '', password: '' }))).toEqual([
      'Введіть email',
      'Введіть пароль',
    ]);
  });

  it('preserves the malformed staff email message', () => {
    expect(messages(staffLoginSchema.safeParse({ email: 'invalid', password: 'secret' }))).toEqual([
      'Невірний email',
    ]);
  });

  it('uses field-specific Ukrainian messages for empty consumer registration fields', () => {
    const result = consumerRegisterSchema.safeParse({
      fullName: '',
      phone: '',
      password: '',
      email: '',
    });
    const resultMessages = result.success
      ? []
      : result.error.issues.map((issue) => issue.message);

    expect(resultMessages).toContain("Введіть ім'я та прізвище");
    expect(resultMessages).toContain('Введіть номер телефону');
    expect(resultMessages).toContain('Введіть пароль');
    expect(resultMessages).not.toContain('Required');
  });
});
