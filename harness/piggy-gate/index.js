// 小猪电脑助手的"安全闸门"：每一个工具调用（运行命令、写文件、上网……）都必须先经过用户点头。
// 通过 ACP 的 session/request_permission 问小猪界面；不同意、取消、没人回答，一律不执行。
export const name = 'piggy-gate'
export const inject = ['tools', 'approval']

// 纯粹的内部记账工具，不碰电脑，不用问
const NO_ASK = new Set(['todo_write'])

function denied(outcome) {
  const message = outcome === 'rejected'
    ? 'This action was not allowed: either the user said no, or Piggy\'s safety rules block it (formatting disks, changing system files or the registry, disabling security, downloading and running programs, etc.). Do not try it again or work around it; explain in plain words what the user can do instead.'
    : `This action was not approved (${outcome}). Do not retry it.`
  return { content: [{ type: 'text', text: 'Error: ' + message }], isError: true, error: { message, info: { name: 'PiggyGate', code: 'PIGGY_DENIED' } } }
}

export function apply(ctx) {
  // 告诉小猪界面：闸门已经装好了（界面看不到这个文件就不会让 AI 动手）
  if (process.env.PIGGY_GATE_MARKER) {
    import('node:fs').then(fs => fs.writeFileSync(process.env.PIGGY_GATE_MARKER, String(process.pid))).catch(() => {})
  }
  ctx.on('tools/execute', async (exec, next) => {
    if (NO_ASK.has(exec.name)) return next()
    if (!exec.agent) return denied('unavailable')
    let outcome
    try {
      outcome = await ctx.approval.request({
        agent: exec.agent,
        toolName: exec.name,
        callId: exec.rootCallId ?? exec.callId,
        reason: 'Piggy Helper asks the user before every action.',
        signal: exec.signal,
      })
    } catch {
      outcome = 'unavailable'
    }
    if (outcome !== 'allowed-once') return denied(outcome)
    return next()
  })
}
