// Pool to avoid allocations (from libuv2k & Mirror)
using System;
using System.Collections.Generic;

namespace PureRpc.Transport.Kcp
{
    /// <summary>
    /// 通用对象池，避免分配开销 / Generic object pool to avoid allocation overhead.
    /// 提供对象的获取、归还和清除功能 / 
    /// Provides object take, return, and clear functionality.
    /// </summary>
    /// <typeparam name="T">池化对象类型 / The type of objects to pool.</typeparam>
    public class Pool<T>
    {
        private readonly Stack<T> objects = new Stack<T>();

        /// <summary>
        /// 对象创建委托 / Object creation delegate.
        /// </summary>
        readonly Func<T> objectGenerator;

        /// <summary>
        /// 对象重置委托 / Object reset delegate.
        /// </summary>
        readonly Action<T> objectResetter;

        /// <summary>
        /// 初始化对象池 / Initializes the object pool.
        /// </summary>
        /// <param name="objectGenerator">对象创建委托 / Object creation delegate.</param>
        /// <param name="objectResetter">对象重置委托 / Object reset delegate.</param>
        /// <param name="initialCapacity">初始池容量 / Initial pool capacity.</param>
        public Pool(Func<T> objectGenerator, Action<T> objectResetter, int initialCapacity)
        {
            this.objectGenerator = objectGenerator;
            this.objectResetter = objectResetter;

            // allocate an initial pool so we have fewer (if any)
            // allocations in the first few frames (or seconds).
            for (int i = 0; i < initialCapacity; ++i)
                objects.Push(objectGenerator());
        }

        /// <summary>
        /// 从池中取出一个对象，如果池为空则创建新对象 / 
        /// Takes an object from the pool, or creates a new one if empty.
        /// </summary>
        /// <returns>池中的或新创建的对象 / An object from the pool or a new one.</returns>
        public T Take() => objects.Count > 0 ? objects.Pop() : objectGenerator();

        /// <summary>
        /// 将对象归还到池中 / Returns an object to the pool.
        /// </summary>
        /// <param name="item">要归还的对象 / The object to return.</param>
        public void Return(T item)
        {
            objectResetter(item);
            objects.Push(item);
        }

        /// <summary>
        /// 清除池中所有对象 / Clears all objects from the pool.
        /// </summary>
        public void Clear() => objects.Clear();

        /// <summary>
        /// 获取池中对象的数量 / Gets the number of objects in the pool.
        /// </summary>
        public int Count => objects.Count;
    }
}
