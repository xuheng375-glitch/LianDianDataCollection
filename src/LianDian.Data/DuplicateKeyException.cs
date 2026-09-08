using System;

namespace LianDian.Data
{
    /// <summary>唯一键（uk_batch）冲突领域异常，业务层据此做幂等/报错处理。</summary>
    public sealed class DuplicateKeyException : Exception
    {
        public DuplicateKeyException(string message)
            : base(message)
        {
        }

        public DuplicateKeyException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
